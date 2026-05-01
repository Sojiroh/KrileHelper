using KrileHelper.UI.Models;
using Translation.Core;

namespace KrileHelper.UI.Services;

public static class TranslatorFactory
{
    public const string GoogleFree = "GoogleFree";
    public const string DeepL = "DeepL";

    public static IReadOnlyList<EngineOption> Available { get; } = new[]
    {
        new EngineOption(GoogleFree, "Google Translate (free)"),
        new EngineOption(DeepL, "DeepL (API key)"),
    };

    public static ITranslator Create(TranslationSettings s)
    {
        return s.Engine switch
        {
            DeepL when !string.IsNullOrWhiteSpace(s.DeepLApiKey)
                => new DeepLTranslator(s.DeepLApiKey),
            _ => new GoogleFreeTranslator(),
        };
    }

    /// <summary>True if a settings change between <paramref name="a"/> and <paramref name="b"/> requires rebuilding the translator.</summary>
    public static bool NeedsRebuild(TranslationSettings a, TranslationSettings b) =>
        a.Engine != b.Engine || a.DeepLApiKey != b.DeepLApiKey;
}

public sealed record EngineOption(string Id, string Display)
{
    public override string ToString() => Display;
}
