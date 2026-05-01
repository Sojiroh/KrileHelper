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
    private readonly SemaphoreSlim _translationSlots = new(MaxConcurrentTranslations, MaxConcurrentTranslations);
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _reattachTimer;
    private readonly CancellationTokenSource _cts = new();
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
        _reattachTimer.Tick += (_, _) => _ = AttachAsync();

        ApplyVisualSettings();
        Settings.Changed += (_, _) =>
        {
            ApplyVisualSettings();
            RebuildTranslatorIfNeeded();
        };

        Hint = BuildPlatformHint();
        _ = AttachAsync();
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
        Status = "Looking for ffxiv_dx11.exe…";
        try
        {
            await _client.AttachAsync();
            _client.ChatLog.Poll();
            ProcessInfo = $"PID {_client.Process.Pid}  base 0x{_client.Process.ModuleBase:X}";
            Status = $"Attached. Translator: {_translator.Name} → {Settings.Current.Translation.TargetLanguage}";
            _reattachTimer.Stop();
            _pollTimer.Start();
        }
        catch (FFXIVNotRunningException)
        {
            Status = "FF14 not running — retrying every 3s…";
            _reattachTimer.Start();
        }
        catch (SignatureScanFailedException ex)
        {
            Status = $"Signature '{ex.SignatureKey}' not found — resources may be outdated.";
            _reattachTimer.Start();
        }
        catch (Exception ex)
        {
            Status = $"Attach error: {ex.Message}";
            _reattachTimer.Start();
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        try
        {
            var items = _client.ChatLog.Poll();
            int appended = 0;
            foreach (var item in items)
            {
                var info = Registry.Lookup(item.Code);
                if (info is null) continue;

                var channelPref = Settings.GetChannel(item.Code);
                if (!channelPref.Show) continue;

                var display = ChatLineDisplay.Create(item, info);
                Lines.Add(display);
                appended++;

                if (channelPref.Translate && !string.IsNullOrWhiteSpace(display.Line))
                    _ = TranslateAsync(display);
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
            _pollTimer.Stop();
            Status = "FF14 closed — waiting for restart…";
            ProcessInfo = "";
            _reattachTimer.Start();
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
        await _translationSlots.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            var (speaker, body) = SplitSpeaker(display.Line);
            var toTranslate = string.IsNullOrEmpty(body) ? display.Line : body;
            var src = Settings.Current.Translation.SourceLanguage;
            var tgt = Settings.Current.Translation.TargetLanguage;
            var result = await _translator.TranslateAsync(toTranslate, src, tgt, _cts.Token).ConfigureAwait(false);
            var combined = string.IsNullOrEmpty(speaker)
                ? result.TranslatedText
                : $"{speaker}: {result.TranslatedText}";

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                display.Translation = combined;
                display.TranslationPending = false;
            });
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                display.TranslationError = ex.Message;
                display.TranslationPending = false;
            });
        }
        finally
        {
            _translationSlots.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _pollTimer.Stop();
        _reattachTimer.Stop();
        _client.Dispose();
        _cts.Dispose();
        _translationSlots.Dispose();
    }
}
