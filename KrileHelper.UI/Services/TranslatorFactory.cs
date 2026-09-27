using KrileHelper.UI.Models;
using Translation.Core;

namespace KrileHelper.UI.Services;

public static class TranslatorFactory
{
    public const string GoogleFree = "GoogleFree";
    public const string DeepL = "DeepL";

    public static IReadOnlyList<EngineOption> Available { get; } = TranslationEngines.All
        .Select(engine => new EngineOption(engine.Id, engine.DisplayName)).ToArray();

    public static ITranslator Create(TranslationSettings settings) =>
        new CachedTranslator(TranslationEngines.Create(settings.Engine, settings.GetProvider()));

    public static string ConfigurationKey(TranslationSettings settings)
    {
        var provider = settings.GetProvider();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            settings.Engine, provider.ApiKey, provider.Region, provider.Model, provider.Endpoint,
        });
    }
}

public sealed record EngineOption(string Id, string Display)
{
    public override string ToString() => Display;
}
