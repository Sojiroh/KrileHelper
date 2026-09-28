using Translation.Core;

namespace KrileHelper.UI.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 2;
    public TranslationSettings Translation { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public DialogueSettings Dialogue { get; set; } = new();
    public ReferenceSettings Reference { get; set; } = new();
    public List<ChatTabSettings> ChatTabs { get; set; } = [new() { Id = "all", Name = "All", IncludeAllChannels = true }];
    public string SelectedChatTabId { get; set; } = "all";

    /// <summary>
    /// Per-chat-code overrides. Only codes the user has touched are persisted;
    /// missing entries fall back to each channel's bundled defaults.
    /// </summary>
    public Dictionary<string, ChannelSetting> Channels { get; set; } = new();
}

public sealed class ChatTabSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New tab";
    public bool IncludeAllChannels { get; set; }
    public List<string> Channels { get; set; } = new();
}

public sealed class HotkeySettings
{
    public bool ToggleOverlayEnabled { get; set; } = true;
    public string ToggleOverlayShortcut { get; set; } = "Ctrl+Alt+Space";
}

public sealed class TranslationSettings
{
    public string Engine { get; set; } = "GoogleFree";
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "es";
    public Dictionary<string, ProviderSettings> Providers { get; set; } = new(StringComparer.Ordinal);
    public bool TranslateNpcNames { get; set; }
    public bool TranslatePlayerNames { get; set; }

    public ProviderSettings GetProvider() =>
        Providers.TryGetValue(Engine, out var settings) ? settings : new ProviderSettings();
}

public sealed class DialogueSettings
{
    public bool Enabled { get; set; } = true;
    public bool OverlayEnabled { get; set; }
}

public sealed class ReferenceSettings
{
    public bool Enabled { get; set; }
    public string GameLanguage { get; set; } = "en";
}

public sealed class WindowSettings
{
    public int? X { get; set; }
    public int? Y { get; set; }
    public int Width { get; set; } = 520;
    public int Height { get; set; } = 640;
    public double BackgroundOpacity { get; set; } = 0.80;
    public bool Topmost { get; set; } = true;
}

public sealed class ChannelSetting
{
    public bool Show { get; set; } = true;
    public bool Translate { get; set; } = true;
}
