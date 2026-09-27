namespace Translation.Core;

/// <summary>Configuration for a translation provider.</summary>
public sealed class ProviderSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;

    public ProviderSettings() { }

    public ProviderSettings(string apiKey = "", string region = "", string model = "", string endpoint = "")
    {
        ApiKey = apiKey ?? string.Empty;
        Region = region ?? string.Empty;
        Model = model ?? string.Empty;
        Endpoint = endpoint ?? string.Empty;
    }

    internal ProviderSettings Normalized() => new((ApiKey ?? string.Empty).Trim(), (Region ?? string.Empty).Trim(), (Model ?? string.Empty).Trim(), (Endpoint ?? string.Empty).Trim());
}

/// <summary>Metadata shown by the engine picker.</summary>
public sealed record ProviderDescriptor(
    string Id,
    string DisplayName,
    bool RequiresApiKey,
    bool SupportsEndpoint,
    bool SupportsModel,
    bool SupportsRegion,
    bool SupportsApiKey,
    string DefaultModel = "",
    string DefaultEndpoint = "",
    string DefaultRegion = "",
    string Help = "")
{
}

/// <summary>Public catalog and factory for all built-in translation engines.</summary>
public static class TranslationEngines
{
    public static IReadOnlyList<ProviderDescriptor> All { get; } = new[]
    {
        new ProviderDescriptor("GoogleFree", "Google Translate (free)", false, false, false, false, false,
            Help: "Unauthenticated Google endpoint; may be rate-limited."),
        new ProviderDescriptor("DeepL", "DeepL API", true, false, false, false, true,
            Help: "DeepL API key; keys ending in :fx use the Free tier."),
        new ProviderDescriptor("DeepLFree", "DeepL (free)", false, false, false, false, false,
            Help: "Unofficial free DeepL web endpoint; subject to service limits."),
        new ProviderDescriptor("Papago", "Papago", false, false, false, false, false),
        new ProviderDescriptor("Azure", "Azure Translator", true, false, false, true, true,
            Help: "Region is the Azure resource region."),
        new ProviderDescriptor("GoogleCloud", "Google Cloud Translate", true, false, false, false, true),
        new ProviderDescriptor("OpenAI", "OpenAI", true, true, true, false, true,
            DefaultModel: "gpt-4o-mini", DefaultEndpoint: "https://api.openai.com/v1/chat/completions"),
        new ProviderDescriptor("DeepSeek", "DeepSeek", true, true, true, false, true,
            DefaultModel: "deepseek-chat", DefaultEndpoint: "https://api.deepseek.com/chat/completions"),
        new ProviderDescriptor("YandexFree", "Yandex (free)", false, false, false, false, false),
        new ProviderDescriptor("YandexCloud", "Yandex Cloud Translate", true, false, false, true, true,
            Help: "Region is the Yandex Cloud folder ID."),
        new ProviderDescriptor("YandexGPT", "YandexGPT", true, false, true, true, true,
            DefaultModel: "yandexgpt-lite/latest", Help: "Region is the Yandex Cloud folder ID."),
        new ProviderDescriptor("Gemini", "Gemini", true, false, true, false, true,
            DefaultModel: "gemini-3.6-flash"),
        new ProviderDescriptor("OpenRouter", "OpenRouter", true, true, true, false, true,
            DefaultModel: "openai/gpt-4o-mini", DefaultEndpoint: "https://openrouter.ai/api/v1/chat/completions"),
        new ProviderDescriptor("Claude", "Claude", true, false, true, false, true,
            DefaultModel: "claude-haiku-4-5"),
        new ProviderDescriptor("LibreTranslate", "LibreTranslate", false, true, false, false, true,
            DefaultEndpoint: "http://localhost:5000/translate", Help: "Endpoint is required (self-hosted or trusted instance); API key is optional."),
        new ProviderDescriptor("Ollama", "Ollama", false, true, true, false, true,
            DefaultModel: "llama3.1", DefaultEndpoint: "http://localhost:11434/v1/chat/completions",
            Help: "Local-only by default; no cloud fallback."),
        new ProviderDescriptor("LMStudio", "LM Studio", false, true, true, false, true,
            DefaultModel: "local-model", DefaultEndpoint: "http://localhost:1234/v1/chat/completions",
            Help: "Local-only by default; no cloud fallback."),
    };

    private static readonly IReadOnlyDictionary<string, ProviderDescriptor> ById =
        All.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

    public static ITranslator Create(string engineId, ProviderSettings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(engineId) || !ById.ContainsKey(engineId.Trim()))
            throw new ArgumentException($"Unknown translation engine '{engineId}'.", nameof(engineId));

        var canonicalId = ById[engineId.Trim()].Id;
        var normalized = (settings ?? new ProviderSettings()).Normalized();
        return canonicalId switch
        {
            "GoogleFree" => new GoogleFreeTranslator(),
            "DeepL" => new DeepLTranslator(normalized.ApiKey),
            "DeepLFree" => new DeepLFreeTranslator(),
            "Papago" => new PapagoTranslator(),
            "Azure" => new AzureTranslator(normalized),
            "GoogleCloud" => new GoogleCloudTranslator(normalized),
            "OpenAI" => new OpenAITranslator(normalized),
            "DeepSeek" => new DeepSeekTranslator(normalized),
            "YandexFree" => new YandexFreeTranslator(),
            "YandexCloud" => new YandexCloudTranslator(normalized),
            "YandexGPT" => new YandexGptTranslator(normalized),
            "Gemini" => new GeminiTranslator(normalized),
            "OpenRouter" => new OpenRouterTranslator(normalized),
            "Claude" => new ClaudeTranslator(normalized),
            "LibreTranslate" => new LibreTranslateTranslator(normalized),
            "Ollama" => new OllamaTranslator(normalized),
            "LMStudio" => new LmStudioTranslator(normalized),
            _ => throw new ArgumentException($"Unknown translation engine '{engineId}'.", nameof(engineId)),
        };
    }
}
