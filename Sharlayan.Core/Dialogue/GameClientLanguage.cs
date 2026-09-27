namespace Sharlayan.Core.Dialogue;

/// <summary>Best-effort game client language detection; unknown is empty.</summary>
public static class GameClientLanguage
{
    public static string Detect(string? executablePath = null)
    {
        var configured = Environment.GetEnvironmentVariable("FFXIV_GAME_LANGUAGE");
        if (!string.IsNullOrWhiteSpace(configured)) return Normalize(configured);
        if (string.IsNullOrWhiteSpace(executablePath)) return string.Empty;

        // Proton stores the Windows game configuration alongside the prefix. Walk
        // upward only from the executable path; never guess from translated text.
        var directory = new DirectoryInfo(Path.GetDirectoryName(executablePath)!);
        for (var i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
        {
            var candidates = new[]
            {
                Path.Combine(directory.FullName, "My Games", "FINAL FANTASY XIV - A Realm Reborn", "FFXIV.cfg"),
                Path.Combine(directory.FullName, "my games", "FINAL FANTASY XIV - A Realm Reborn", "FFXIV.cfg")
            };
            foreach (var candidate in candidates)
            {
                try { if (File.Exists(candidate)) return Parse(File.ReadAllText(candidate)); }
                catch { /* process attachment must remain independent of config access */ }
            }
        }
        return string.Empty;
    }

    public static string Normalize(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "0" or "JA" or "JAPANESE" => "ja",
        "1" or "EN" or "ENGLISH" => "en",
        "2" or "DE" or "GERMAN" => "de",
        "3" or "FR" or "FRENCH" => "fr",
        _ => string.Empty
    };

    public static string Parse(string? configuration)
    {
        if (string.IsNullOrEmpty(configuration)) return string.Empty;
        foreach (var raw in configuration.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("Language", StringComparison.Ordinal)) continue;
            var value = line["Language".Length..].Trim();
            if (!int.TryParse(value, out var code)) continue;
            return code switch
            {
                0 => "ja",
                1 => "en",
                2 => "de",
                3 => "fr",
                _ => string.Empty
            };
        }
        return string.Empty;
    }
}
