namespace Translation.Core;

internal static class LanguageMap
{
    // DeepL accepts most ISO codes verbatim (uppercased), but a few targets
    // need a regional variant ("EN-US" not "EN", "PT-PT" not "PT", "ZH-HANS" for
    // simplified Chinese in newer API). Map our UI codes to DeepL's expected form.

    private static readonly Dictionary<string, string> TargetMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["es"] = "ES",
        ["en"] = "EN-US",
        ["ja"] = "JA",
        ["zh-CN"] = "ZH-HANS",
        ["zh-TW"] = "ZH-HANT",
        ["ko"] = "KO",
        ["de"] = "DE",
        ["fr"] = "FR",
        ["pt"] = "PT-PT",
        ["it"] = "IT",
        ["ru"] = "RU",
        ["nl"] = "NL",
        ["pl"] = "PL",
        ["tr"] = "TR",
        ["uk"] = "UK",
        // Targets DeepL does NOT support (Catalan, Arabic, Hindi, Vietnamese, Thai)
        // intentionally absent; ToDeepLTarget returns null for those.
    };

    private static readonly Dictionary<string, string> SourceMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["es"] = "ES",
        ["en"] = "EN",
        ["ja"] = "JA",
        ["zh-CN"] = "ZH",
        ["zh-TW"] = "ZH",
        ["ko"] = "KO",
        ["de"] = "DE",
        ["fr"] = "FR",
        ["pt"] = "PT",
        ["it"] = "IT",
        ["ru"] = "RU",
        ["nl"] = "NL",
        ["pl"] = "PL",
        ["tr"] = "TR",
        ["uk"] = "UK",
    };

    public static string? ToDeepLTarget(string code) =>
        TargetMap.TryGetValue(code, out var v) ? v : null;

    /// <summary>Returns null when "auto" was requested (DeepL auto-detects when source_lang omitted).</summary>
    public static string? ToDeepLSource(string code) =>
        string.Equals(code, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : SourceMap.GetValueOrDefault(code);
}
