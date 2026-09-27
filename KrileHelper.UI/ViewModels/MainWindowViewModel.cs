using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Sharlayan.Core;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;
using Translation.Core;
using Sharlayan.Core.ChatLog;
using Sharlayan.Core.Dialogue;

namespace KrileHelper.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const int MaxRetainedLines = 500;
    private const int MaxConcurrentTranslations = 4;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
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
    private readonly ChatTranslationService _chatTranslation;
    private int _translationGeneration;
    private string _overlayText = "";
    private string _overlaySpeaker = "";
    private string _overlayCode = "";
    private readonly Dictionary<(string Code, string Speaker, string Text, int Generation), Task<ChatTranslation>> _pendingTranslations = new();

    public ReferenceTranslationService ReferenceTranslations { get; } = new();
    public GameAssets Assets { get; } = new();
    public LiveDialogueSnapshot? CurrentDialogue { get; private set; }
    public ChatTranslation? CurrentDialogueTranslation { get; private set; }
    public int AttachedPid => _client.IsAttached ? _client.Process.Pid : 0;
    public event Action? DialogueUpdated;

    public SettingsService Settings { get; }
    public ChatCodeRegistry Registry { get; }

    [ObservableProperty] private string _status = "Initializing…";
    [ObservableProperty] private string _processInfo = "";
    [ObservableProperty] private string? _hint;
    [ObservableProperty] private string _dialogueStatus = "";
    [ObservableProperty] private IBrush _backgroundBrush = Brushes.Transparent;
    [ObservableProperty] private bool _topmost = true;

    public ObservableCollection<ChatLineDisplay> Lines { get; } = new();
    public event EventHandler? LinesAppended;

    public MainWindowViewModel(SettingsService settings, ChatCodeRegistry registry)
    {
        Settings = settings;
        Registry = registry;
        _chatTranslation = new ChatTranslationService(ReferenceTranslations);
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
            _translationGeneration++;
            _overlayText = "";
            CurrentDialogueTranslation = null;
            DialogueUpdated?.Invoke();
        };
        Settings.Changed += _settingsChangedHandler;

        Hint = BuildPlatformHint();
        TrackTask(AttachAsync());
    }

    private static string EngineKey(TranslationSettings s) => TranslatorFactory.ConfigurationKey(s);

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
                await Task.Run(() => Assets.Load(_client.GameExecutablePath), _cts.Token);
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
            int appended = 0;
            if (Settings.Current.Dialogue.Enabled)
            {
                CurrentDialogue = _client.Dialogue.Poll();
                foreach (var item in CurrentDialogue.Lines)
                    if (Append(item)) appended++;
            }
            else
            {
                CurrentDialogue = null;
                CurrentDialogueTranslation = null;
                _overlayText = "";
            }
            foreach (var item in _client.ChatLog.Poll())
            {
                if (Settings.Current.Dialogue.Enabled && _client.Dialogue.ShouldSuppressChat(item)) continue;
                if (Append(item)) appended++;
            }
            UpdateDialogueTranslation();
            DialogueUpdated?.Invoke();
            if (appended == 0) return;
            while (Lines.Count > MaxRetainedLines) Lines.RemoveAt(0);
            LinesAppended?.Invoke(this, EventArgs.Empty);
        }
        catch (ProcessDetachedException)
        {
            StopPollTimerSafe();
            Status = "FF14 closed — waiting for restart…";
            ProcessInfo = "";
            CurrentDialogue = null;
            CurrentDialogueTranslation = null;
            DialogueUpdated?.Invoke();
            StartReattachTimerSafe();
        }
        catch (Exception ex)
        {
            Status = $"Poll error: {ex.Message}";
            CurrentDialogue = null;
            DialogueUpdated?.Invoke();
        }
    }

    private bool Append(ChatLogItem item)
    {
        var info = Registry.Lookup(item.Code);
        if (info is null) return false;
        var channel = Settings.GetChannel(info);
        if (!channel.Show) return false;
        var display = ChatLineDisplay.Create(item, info);
        display.Assets = Assets;
        Lines.Add(display);
        if (channel.Translate && !string.IsNullOrWhiteSpace(display.Line))
            TrackTask(TranslateAsync(display));
        else
            display.TranslationPending = false;
        return true;
    }

    private ChatTranslationContext TranslationContext(string code)
    {
        var translation = Settings.Current.Translation;
        var gameLanguage = _client.GameLanguage;
        var referenceLanguage = translation.SourceLanguage != "auto" ? translation.SourceLanguage :
            string.IsNullOrEmpty(gameLanguage) ? Settings.Current.Reference.GameLanguage : gameLanguage;
        return new ChatTranslationContext(translation.SourceLanguage, translation.TargetLanguage,
            translation.TranslateNpcNames, translation.TranslatePlayerNames,
            Settings.Current.Reference.Enabled && (ChatTranslationService.IsNpcChannel(code) || code == "0039"),
            referenceLanguage, _client.PlayerName, _client.PlayerIsFeminine, Assets.Worlds);
    }

    private void UpdateDialogueTranslation()
    {
        var current = CurrentDialogue;
        if (current is not { IsVisible: true } || !Settings.Current.Dialogue.OverlayEnabled)
        {
            CurrentDialogueTranslation = null;
            _overlayText = "";
            return;
        }
        var info = Registry.Lookup(current.Code);
        if (info is null || Settings.GetChannel(info) is not { Show: true, Translate: true }) return;
        if (_overlayText == current.Text && _overlaySpeaker == current.Speaker && _overlayCode == current.Code) return;
        _overlayText = current.Text;
        _overlaySpeaker = current.Speaker;
        _overlayCode = current.Code;
        CurrentDialogueTranslation = null;
        if (current.Surface != DialogueSurface.Bubble && current.Text.Length > 0)
            TrackTask(TranslateOverlayAsync(current, _translationGeneration));
    }

    private async Task TranslateOverlayAsync(LiveDialogueSnapshot snapshot, int generation)
    {
        try
        {
            var translated = await GetTranslationTask(snapshot.Text, snapshot.Code, snapshot.Speaker).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || generation != _translationGeneration || CurrentDialogue?.Text != snapshot.Text ||
                    CurrentDialogue.Speaker != snapshot.Speaker || CurrentDialogue.Code != snapshot.Code) return;
                CurrentDialogueTranslation = translated;
                DialogueUpdated?.Invoke();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed) Status = $"Dialogue translation error: {ex.Message}";
            });
        }
    }


    private async Task TranslateAsync(ChatLineDisplay display)
    {
        try
        {
            var result = await GetTranslationTask(display.Line, display.Code).ConfigureAwait(false);
            if (_disposed || _cts.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed) return;
                display.Translation = result.Text;
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
    }

    private Task<ChatTranslation> GetTranslationTask(string text, string code, string? explicitSpeaker = null)
    {
        var (speaker, body) = explicitSpeaker is null
            ? ChatTranslationService.SplitSpeaker(text, code) : (explicitSpeaker, text);
        var key = (code, speaker, body, _translationGeneration);
        if (_pendingTranslations.TryGetValue(key, out var existing)) return existing;
        var task = TranslateQueuedAsync(_translator, body, code, speaker, TranslationContext(code));
        _pendingTranslations.Add(key, task);
        _ = task.ContinueWith(_ => Dispatcher.UIThread.Post(() => _pendingTranslations.Remove(key)),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    private async Task<ChatTranslation> TranslateQueuedAsync(ITranslator translator, string text, string code,
        string speaker, ChatTranslationContext context)
    {
        await _translationSlots.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            return await _chatTranslation.TranslateAsync(translator, text, code, context, _cts.Token, speaker)
                .ConfigureAwait(false);
        }
        finally
        {
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
        ReferenceTranslations.Dispose();
        Assets.Dispose();
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
