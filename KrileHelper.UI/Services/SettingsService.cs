using System.Text.Json;
using KrileHelper.UI.Models;

namespace KrileHelper.UI.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public string FilePath { get; }
    public AppSettings Current { get; private set; } = new();

    public event EventHandler? Changed;

    public SettingsService(string? overridePath = null)
    {
        FilePath = overridePath ?? DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        MigrateLegacyConfig();
        Load();
    }

    private static string DefaultPath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = !string.IsNullOrEmpty(xdg) ? xdg : Path.Combine(home, ".config");
        return Path.Combine(root, "krile-helper", "settings.json");
    }

    // The project was renamed from "tataru-helper" to "krile-helper". If the
    // user already had a tataru-helper config, copy it over once so they don't
    // lose their DeepL key, channel toggles, window position, etc.
    private void MigrateLegacyConfig()
    {
        if (File.Exists(FilePath)) return;
        var legacyPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(FilePath)!)!, "tataru-helper", "settings.json");
        if (!File.Exists(legacyPath)) return;
        try { File.Copy(legacyPath, FilePath); } catch { /* best-effort */ }
    }

    public void Load()
    {
        if (!File.Exists(FilePath))
        {
            Current = new AppSettings();
            return;
        }
        try
        {
            var json = File.ReadAllText(FilePath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch
        {
            // Corrupt file -> back it up and start fresh rather than crashing.
            try { File.Move(FilePath, FilePath + ".bad", overwrite: true); } catch { /* ignore */ }
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOpts));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // Settings save failures shouldn't crash the app.
        }
    }

    public void NotifyChanged()
    {
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ChannelSetting GetChannel(ChatCodeInfo info)
    {
        if (Current.Channels.TryGetValue(info.Code, out var s)) return s;
        return GetDefaultChannel(info);
    }

    public static ChannelSetting GetDefaultChannel(ChatCodeInfo info) => new()
    {
        Show = true,
        Translate = info.TranslateByDefault,
    };
}
