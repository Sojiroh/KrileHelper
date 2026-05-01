using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Sharlayan.Core;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;
using Translation.Core;

namespace KrileHelper.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const int MaxRetainedLines = 500;
    private const int MaxConcurrentTranslations = 4;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ReattachInterval = TimeSpan.FromSeconds(3);

    private readonly MemoryClient _client = new();
    private ITranslator _translator;
    private string _lastEngineKey = "";
    private readonly SemaphoreSlim _attachGate = new(1, 1);
    private readonly SemaphoreSlim _translationSlots = new(MaxConcurrentTranslations, MaxConcurrentTranslations);
    private readonly object _activeTasksGate = new();
    private readonly HashSet<Task> _activeTasks = new();
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _reattachTimer;
    private readonly CancellationTokenSource _cts = new();
    private readonly EventHandler _settingsChangedHandler;
    private bool _disposed;

    public SettingsService Settings { get; }
    public ChatCodeRegistry Registry { get; }

    [ObservableProperty] private string _status = "Initializing…";
    [ObservableProperty] private string _processInfo = "";
    [ObservableProperty] private string? _hint;
    [ObservableProperty] private IBrush _backgroundBrush = Brushes.Transparent;
    [ObservableProperty] private bool _topmost = true;

    public ObservableCollection<ChatLineDisplay> Lines { get; } = new();
    public event EventHandler? LinesAppended;

    public MainWindowViewModel(SettingsService settings, ChatCodeRegistry registry)
    {
        Settings = settings;
        Registry = registry;
        _translator = TranslatorFactory.Create(settings.Current.Translation);
        _lastEngineKey = EngineKey(settings.Current.Translation);

        _pollTimer = new DispatcherTimer { Interval = PollInterval };
        _pollTimer.Tick += OnPollTick;

        _reattachTimer = new DispatcherTimer { Interval = ReattachInterval };
        _reattachTimer.Tick += (_, _) => TrackTask(AttachAsync());

        ApplyVisualSettings();
        _settingsChangedHandler = (_, _) =>
        {
            if (_disposed) return;
            ApplyVisualSettings();
            RebuildTranslatorIfNeeded();
        };
        Settings.Changed += _settingsChangedHandler;

        Hint = BuildPlatformHint();
        TrackTask(AttachAsync());
    }

    private static string EngineKey(TranslationSettings s) => $"{s.Engine}|{s.DeepLApiKey}";

    private void RebuildTranslatorIfNeeded()
    {
        var key = EngineKey(Settings.Current.Translation);
        if (key == _lastEngineKey) return;
        _translator = TranslatorFactory.Create(Settings.Current.Translation);
        _lastEngineKey = key;
        Status = $"Translator switched to {_translator.Name} → {Settings.Current.Translation.TargetLanguage}";
    }

    private void ApplyVisualSettings()
    {
        // Base panel color (#1A1A1F) with user-controlled alpha.
        byte alpha = (byte)Math.Clamp(Settings.Current.Window.BackgroundOpacity * 255.0, 0.0, 255.0);
        BackgroundBrush = new SolidColorBrush(Color.FromArgb(alpha, 0x1A, 0x1A, 0x1F));
        Topmost = Settings.Current.Window.Topmost;
    }

    private static string? BuildPlatformHint()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return null;
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase))
            return "Wayland: if this window doesn't appear over FFXIV, switch the game to Borderless Windowed.";
        return null;
    }

    private async Task AttachAsync()
    {
        if (_disposed) return;
        bool entered = false;
        try
        {
            entered = await _attachGate.WaitAsync(0, _cts.Token);
            if (!entered || _disposed) return;

            Status = "Looking for ffxiv_dx11.exe…";
            try
            {
                await _client.AttachAsync(_cts.Token);
                _client.ChatLog.Poll();
                ProcessInfo = $"PID {_client.Process.Pid}  base 0x{_client.Process.ModuleBase:X}";
                Status = $"Attached. Translator: {_translator.Name} → {Settings.Current.Translation.TargetLanguage}";
                StopReattachTimerSafe();
                StartPollTimerSafe();
            }
            catch (FFXIVNotRunningException)
            {
                StopPollTimerSafe();
                Status = "FF14 not running — retrying every 3s…";
                StartReattachTimerSafe();
            }
            catch (SignatureScanFailedException ex)
            {
                StopPollTimerSafe();
                Status = $"Signature '{ex.SignatureKey}' not found — resources may be outdated.";
                StartReattachTimerSafe();
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                StopPollTimerSafe();
                Status = $"Attach error: {ex.Message}";
                StartReattachTimerSafe();
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        finally
        {
            if (entered)
                _attachGate.Release();
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (_disposed) return;

        try
        {
            var items = _client.ChatLog.Poll();
            int appended = 0;
            foreach (var item in items)
            {
                var info = Registry.Lookup(item.Code);
                if (info is null) continue;

                var channelPref = Settings.GetChannel(info);
                if (!channelPref.Show) continue;

                var display = ChatLineDisplay.Create(item, info);
                Lines.Add(display);
                appended++;

                if (channelPref.Translate && !string.IsNullOrWhiteSpace(display.Line))
                    TrackTask(TranslateAsync(display));
                else
                    display.TranslationPending = false;
            }

            if (appended == 0) return;

            while (Lines.Count > MaxRetainedLines)
                Lines.RemoveAt(0);

            LinesAppended?.Invoke(this, EventArgs.Empty);
        }
        catch (ProcessDetachedException)
        {
            StopPollTimerSafe();
            Status = "FF14 closed — waiting for restart…";
            ProcessInfo = "";
            StartReattachTimerSafe();
        }
        catch (Exception ex)
        {
            Status = $"Poll error: {ex.Message}";
        }
    }

    private static (string Speaker, string Body) SplitSpeaker(string line)
    {
        int idx = line.IndexOf(':');
        if (idx <= 0 || idx > 40) return ("", line);
        return (line[..idx], line[(idx + 1)..].TrimStart());
    }

    private async Task TranslateAsync(ChatLineDisplay display)
    {
        bool entered = false;
        try
        {
            await _translationSlots.WaitAsync(_cts.Token).ConfigureAwait(false);
            entered = true;

            var (speaker, body) = SplitSpeaker(display.Line);
            var toTranslate = string.IsNullOrEmpty(body) ? display.Line : body;
            var src = Settings.Current.Translation.SourceLanguage;
            var tgt = Settings.Current.Translation.TargetLanguage;
            var result = await _translator.TranslateAsync(toTranslate, src, tgt, _cts.Token).ConfigureAwait(false);
            if (_disposed || _cts.IsCancellationRequested) return;

            var combined = string.IsNullOrEmpty(speaker)
                ? result.TranslatedText
                : $"{speaker}: {result.TranslatedText}";

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed) return;
                display.Translation = combined;
                display.TranslationPending = false;
            });
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            if (_disposed) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed) return;
                display.TranslationError = ex.Message;
                display.TranslationPending = false;
            });
        }
        finally
        {
            if (entered)
                _translationSlots.Release();
        }
    }

    private void TrackTask(Task task)
    {
        lock (_activeTasksGate)
        {
            _activeTasks.Add(task);
        }

        task.ContinueWith(static (completedTask, state) =>
        {
            var viewModel = (MainWindowViewModel)state!;
            _ = completedTask.Exception;
            lock (viewModel._activeTasksGate)
            {
                viewModel._activeTasks.Remove(completedTask);
            }
        }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void StopPollTimerSafe()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (_pollTimer.IsEnabled)
                _pollTimer.Stop();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_pollTimer.IsEnabled)
                _pollTimer.Stop();
        });
    }

    private void StartPollTimerSafe()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (!_disposed && !_pollTimer.IsEnabled)
                _pollTimer.Start();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && !_pollTimer.IsEnabled)
                _pollTimer.Start();
        });
    }

    private void StopReattachTimerSafe()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (_reattachTimer.IsEnabled)
                _reattachTimer.Stop();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_reattachTimer.IsEnabled)
                _reattachTimer.Stop();
        });
    }

    private void StartReattachTimerSafe()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (!_disposed && !_reattachTimer.IsEnabled)
                _reattachTimer.Start();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && !_reattachTimer.IsEnabled)
                _reattachTimer.Start();
        });
    }

    private Task[] SnapshotActiveTasks()
    {
        lock (_activeTasksGate)
        {
            return _activeTasks.ToArray();
        }
    }

    private void DisposeResources()
    {
        _client.Dispose();
        _attachGate.Dispose();
        _cts.Dispose();
        _translationSlots.Dispose();
    }

    private void DisposeResourcesAfterTasks(Task[] tasks)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                DisposeResources();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Settings.Changed -= _settingsChangedHandler;
        _cts.Cancel();

        StopPollTimerSafe();
        StopReattachTimerSafe();

        var activeTasks = SnapshotActiveTasks();
        if (activeTasks.Length == 0)
        {
            DisposeResources();
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            var all = Task.WhenAll(activeTasks);
            try
            {
                if (all.Wait(TimeSpan.FromSeconds(2)))
                {
                    DisposeResources();
                    return;
                }
            }
            catch
            {
            }
        }

        DisposeResourcesAfterTasks(activeTasks);
    }
}
