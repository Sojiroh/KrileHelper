namespace KrileHelper.UI.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public TranslationSettings Translation { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>
    /// Per-chat-code overrides. Only codes the user has touched are persisted;
    /// missing entries fall back to each channel's bundled defaults.
    /// </summary>
    public Dictionary<string, ChannelSetting> Channels { get; set; } = new();
}

public sealed class HotkeySettings
{
    public bool ToggleOverlayEnabled { get; set; } = true;
    public string ToggleOverlayShortcut { get; set; } = "Ctrl+Alt+Space";
}

public sealed class TranslationSettings
{
    public string Engine { get; set; } = "GoogleFree";   // GoogleFree | DeepL
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "es";
    public string DeepLApiKey { get; set; } = "";
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
