using System.Text;
using System.Text.Json;

namespace Translation.Core;

internal sealed class OpenAIChatClient
{
    private const int MaxAttempts = 3;
    private const int BackoffMilliseconds = 600;
    private readonly string _name, _defaultEndpoint, _defaultModel;
    private readonly ProviderSettings _settings;
    private readonly HttpClient _http;
    private readonly bool _requiresApiKey, _requiresEndpoint;
    private readonly IReadOnlyDictionary<string, string>? _extraHeaders;

    public OpenAIChatClient(string name, string defaultEndpoint, string defaultModel, ProviderSettings settings,
        HttpClient? http = null, bool requiresApiKey = true, IReadOnlyDictionary<string, string>? extraHeaders = null,
        bool requiresEndpoint = false)
    {
        _name = name; _defaultEndpoint = defaultEndpoint; _defaultModel = defaultModel;
        _settings = (settings ?? new ProviderSettings()).Normalized(); _http = http ?? ProviderHttp.Default;
        _requiresApiKey = requiresApiKey; _requiresEndpoint = requiresEndpoint; _extraHeaders = extraHeaders;
    }

    public string CacheScope => $"{_name}:{_settings.ApiKey}:{_settings.Endpoint}:{_settings.Model}";

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text)) return new TranslationResult(string.Empty, sourceLang, _name);
        var apiKey = _settings.ApiKey;
        if (_requiresApiKey) apiKey = ProviderHttp.RequireApiKey(apiKey, _name);
        if (_requiresEndpoint && string.IsNullOrWhiteSpace(_settings.Endpoint))
            throw new TranslationException($"{_name} endpoint is required. Configure it in Settings.");
        var endpoint = ProviderHttp.ValidateEndpoint(ProviderHttp.ChatEndpoint(_settings.Endpoint, _defaultEndpoint), _name);
        var model = string.IsNullOrWhiteSpace(_settings.Model) ? _defaultModel : _settings.Model;
        var payload = new
        {
            model,
            temperature = 0.2,
            messages = new[]
            {
                new { role = "system", content = FfxivTranslationPrompt.Build(sourceLang, targetLang) },
                new { role = "user", content = text }
            }
        };
        var payloadText = JsonSerializer.Serialize(payload);
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payloadText, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
            if (_extraHeaders is not null)
                foreach (var header in _extraHeaders) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            try
            {
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                var body = await ProviderHttp.ReadAsync(response, ct).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status == 429 || (!response.IsSuccessStatusCode && body.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase)))
                    throw new TranslationException($"{_name} quota exceeded (HTTP {status}).");
                if (!response.IsSuccessStatusCode)
                {
                    if (IsTransient(status) && attempt < MaxAttempts) { await DelayAsync(attempt, ct).ConfigureAwait(false); continue; }
                    throw ProviderHttp.HttpError(_name, response, body);
                }
                var parsed = ParseContent(body);
                if (!string.IsNullOrWhiteSpace(parsed)) return new TranslationResult(parsed, sourceLang, _name);
                if (attempt < MaxAttempts) { await DelayAsync(attempt, ct).ConfigureAwait(false); continue; }
                throw new TranslationException($"{_name} returned an empty or malformed translation.");
            }
            catch (TranslationException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                if (attempt >= MaxAttempts) throw ProviderHttp.Network(_name, ex);
                last = ex; await DelayAsync(attempt, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex))
            {
                last = ex; await DelayAsync(attempt, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                last = ex; break;
            }
        }
        if (last is not null) throw ProviderHttp.Network(_name, last);
        throw new TranslationException($"{_name} returned an empty or malformed translation.");
    }

    internal static string ResolveEndpoint(string configured, string fallback) => ProviderHttp.ChatEndpoint(configured, fallback);

    internal static string ParseContent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return string.Empty;
            var choice = choices[0];
            if (choice.ValueKind != JsonValueKind.Object || !choice.TryGetProperty("message", out var message)) return string.Empty;
            return AiResponseSanitizer.Strip(ProviderHttp.JsonText(message, "content"));
        }
        catch (JsonException) { return string.Empty; }
    }

    private static bool IsTransient(int status) => status is 408 or 425 or 429 or >= 500 and <= 599;
    private static bool IsTransient(Exception ex) => ex is HttpRequestException or TaskCanceledException or IOException;
    private static Task DelayAsync(int attempt, CancellationToken ct) => Task.Delay(BackoffMilliseconds * (1 << (attempt - 1)), ct);
}

public sealed class OpenAITranslator : ITranslator
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";
    private readonly OpenAIChatClient _client;
    public string Name => "OpenAI";
    public string CacheScope => _client.CacheScope;
    public OpenAITranslator(ProviderSettings settings, HttpClient? http = null) => _client = new(Name, Endpoint, "gpt-4o-mini", settings, http);
    public Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default) => _client.TranslateAsync(text, sourceLang, targetLang, ct);
}

public sealed class DeepSeekTranslator : ITranslator
{
    private const string Endpoint = "https://api.deepseek.com/chat/completions";
    private readonly OpenAIChatClient _client;
    public string Name => "DeepSeek";
    public string CacheScope => _client.CacheScope;
    public DeepSeekTranslator(ProviderSettings settings, HttpClient? http = null) => _client = new(Name, Endpoint, "deepseek-chat", settings, http);
    public Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default) => _client.TranslateAsync(text, sourceLang, targetLang, ct);
}

public sealed class OpenRouterTranslator : ITranslator
{
    private const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";
    private readonly OpenAIChatClient _client;
    public string Name => "OpenRouter";
    public string CacheScope => _client.CacheScope;
    public OpenRouterTranslator(ProviderSettings settings, HttpClient? http = null) => _client = new(Name, Endpoint, "openai/gpt-4o-mini", settings, http, extraHeaders: new Dictionary<string, string> { ["HTTP-Referer"] = "https://github.com/NightlyRevenger/KrileHelper", ["X-Title"] = "KrileHelper" });
    public Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default) => _client.TranslateAsync(text, sourceLang, targetLang, ct);
}

public sealed class OllamaTranslator : ITranslator
{
    private const string Endpoint = "http://localhost:11434/v1/chat/completions";
    private readonly OpenAIChatClient _client;
    public string Name => "Ollama";
    public string CacheScope => _client.CacheScope;
    public OllamaTranslator(ProviderSettings settings, HttpClient? http = null) => _client = new(Name, Endpoint, "llama3.1", settings, http, requiresApiKey: false, requiresEndpoint: true);
    public Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default) => _client.TranslateAsync(text, sourceLang, targetLang, ct);
}

public sealed class LmStudioTranslator : ITranslator
{
    private const string Endpoint = "http://localhost:1234/v1/chat/completions";
    private readonly OpenAIChatClient _client;
    public string Name => "LMStudio";
    public string CacheScope => _client.CacheScope;
    public LmStudioTranslator(ProviderSettings settings, HttpClient? http = null) => _client = new(Name, Endpoint, "local-model", settings, http, requiresApiKey: false, requiresEndpoint: true);
    public Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default) => _client.TranslateAsync(text, sourceLang, targetLang, ct);
}

internal sealed class GeminiChatClient
{
    private const string EndpointFormat = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";
    private readonly string _name, _defaultModel; private readonly ProviderSettings _settings; private readonly HttpClient _http;
    public GeminiChatClient(string name, string defaultModel, ProviderSettings settings, HttpClient? http = null) { _name = name; _defaultModel = defaultModel; _settings = (settings ?? new ProviderSettings()).Normalized(); _http = http ?? ProviderHttp.Default; }
    public string CacheScope => $"{_name}:{_settings.ApiKey}:{_settings.Model}";

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text)) return new TranslationResult(string.Empty, sourceLang, _name);
        var key = ProviderHttp.RequireApiKey(_settings.ApiKey, _name);
        var model = string.IsNullOrWhiteSpace(_settings.Model) ? _defaultModel : _settings.Model;
        var endpoint = string.Format(EndpointFormat, Uri.EscapeDataString(model));
        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = FfxivTranslationPrompt.Build(sourceLang, targetLang) } } },
            contents = new[] { new { role = "user", parts = new[] { new { text } } } },
            generationConfig = new { temperature = 0.2 }
        };
        var payloadText = JsonSerializer.Serialize(payload);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(payloadText, Encoding.UTF8, "application/json") };
            request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
            try
            {
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                var body = await ProviderHttp.ReadAsync(response, ct).ConfigureAwait(false); var status = (int)response.StatusCode;
                if (status == 429 || (!response.IsSuccessStatusCode && body.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase))) throw new TranslationException($"{_name} quota exceeded (HTTP {status}).");
                if (!response.IsSuccessStatusCode) { if (IsTransient(status) && attempt < 3) { await DelayAsync(attempt, ct); continue; } throw ProviderHttp.HttpError(_name, response, body); }
                var parsed = ParseContent(body); if (!string.IsNullOrWhiteSpace(parsed)) return new TranslationResult(parsed, sourceLang, _name);
                if (attempt < 3) { await DelayAsync(attempt, ct); continue; }
                throw new TranslationException($"{_name} returned an empty or malformed translation.");
            }
            catch (TranslationException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                if (attempt >= 3) throw ProviderHttp.Network(_name, ex);
                await DelayAsync(attempt, ct);
            }
            catch (HttpRequestException ex) when (attempt >= 3) { throw ProviderHttp.Network(_name, ex); }
            catch (Exception ex) when (attempt < 3 && (ex is HttpRequestException or TaskCanceledException or IOException)) { await DelayAsync(attempt, ct); }
        }
        throw new TranslationException($"{_name} returned an empty or malformed translation.");
    }

    internal static string ParseContent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) return string.Empty;
            var candidate = candidates[0];
            if (candidate.ValueKind != JsonValueKind.Object || !candidate.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Object || !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array) return string.Empty;
            var sb = new StringBuilder(); foreach (var part in parts.EnumerateArray()) sb.Append(ProviderHttp.JsonText(part, "text"));
            return AiResponseSanitizer.Strip(sb.ToString());
        }
        catch (JsonException) { return string.Empty; }
    }
    private static bool IsTransient(int status) => status is 408 or 425 or 429 or >= 500 and <= 599;
    private static Task DelayAsync(int attempt, CancellationToken ct) => Task.Delay(600 * (1 << (attempt - 1)), ct);
}

public sealed class GeminiTranslator : ITranslator
{
    private readonly GeminiChatClient _client;
    public string Name => "Gemini";
    public string CacheScope => _client.CacheScope;
    public GeminiTranslator(ProviderSettings settings, HttpClient? http = null) => _client = new(Name, "gemini-3.6-flash", settings, http);
    public Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default) => _client.TranslateAsync(text, sourceLang, targetLang, ct);
}

public sealed class ClaudeTranslator : ITranslator
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private readonly ProviderSettings _settings; private readonly HttpClient _http;
    public string Name => "Claude";
    public string CacheScope => $"{Name}:{_settings.ApiKey}:{_settings.Model}";
    public ClaudeTranslator(ProviderSettings settings, HttpClient? http = null) { _settings = (settings ?? new ProviderSettings()).Normalized(); _http = http ?? ProviderHttp.Default; }

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) return new TranslationResult(string.Empty, sourceLang, Name);
        var key = ProviderHttp.RequireApiKey(_settings.ApiKey, Name);
        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "claude-haiku-4-5" : _settings.Model;
        var payload = new { model, max_tokens = 2000, system = FfxivTranslationPrompt.Build(sourceLang, targetLang), thinking = new { type = "disabled" }, messages = new[] { new { role = "user", content = text } } };
        var payloadText = JsonSerializer.Serialize(payload);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(payloadText, Encoding.UTF8, "application/json") };
            request.Headers.TryAddWithoutValidation("x-api-key", key); request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            try
            {
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                var body = await ProviderHttp.ReadAsync(response, ct).ConfigureAwait(false); var status = (int)response.StatusCode;
                if (status == 429 || (!response.IsSuccessStatusCode && body.Contains("rate_limit_error", StringComparison.OrdinalIgnoreCase))) throw new TranslationException($"{Name} quota exceeded (HTTP {status}).");
                if (!response.IsSuccessStatusCode) { if (IsTransient(status) && attempt < 3) { await DelayAsync(attempt, ct); continue; } throw ProviderHttp.HttpError(Name, response, body); }
                var parsed = ParseContent(body); if (!string.IsNullOrWhiteSpace(parsed)) return new TranslationResult(parsed, sourceLang, Name);
                if (IsRefusal(body)) throw new TranslationException($"{Name} refused the translation request.");
                if (attempt < 3) { await DelayAsync(attempt, ct); continue; }
                throw new TranslationException($"{Name} returned an empty or malformed translation.");
            }
            catch (TranslationException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                if (attempt >= 3) throw ProviderHttp.Network(Name, ex);
                await DelayAsync(attempt, ct);
            }
            catch (HttpRequestException ex) when (attempt >= 3) { throw ProviderHttp.Network(Name, ex); }
            catch (Exception) when (attempt < 3) { await DelayAsync(attempt, ct); }
        }
        throw new TranslationException($"{Name} returned an empty or malformed translation.");
    }

    internal static string ParseContent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("content", out var blocks) || blocks.ValueKind != JsonValueKind.Array) return string.Empty;
            var sb = new StringBuilder(); foreach (var block in blocks.EnumerateArray()) if (ProviderHttp.JsonText(block, "type") == "text") sb.Append(ProviderHttp.JsonText(block, "text"));
            return AiResponseSanitizer.Strip(sb.ToString());
        }
        catch (JsonException) { return string.Empty; }
    }
    internal static bool IsRefusal(string body)
    {
        try { using var doc = JsonDocument.Parse(body); return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("stop_reason", out var reason) && reason.ValueKind == JsonValueKind.String && reason.GetString() == "refusal"; }
        catch (JsonException) { return false; }
    }
    private static bool IsTransient(int status) => status is 408 or 425 or 429 or >= 500 and <= 599;
    private static Task DelayAsync(int attempt, CancellationToken ct) => Task.Delay(600 * (1 << (attempt - 1)), ct);
}

public sealed class YandexGptTranslator : ITranslator
{
    private const string Endpoint = "https://llm.api.cloud.yandex.net/foundationModels/v1/completion";
    private readonly ProviderSettings _settings; private readonly HttpClient _http;
    public string Name => "YandexGPT";
    public string CacheScope => $"{Name}:{_settings.ApiKey}:{_settings.Region}:{_settings.Model}";
    public YandexGptTranslator(ProviderSettings settings, HttpClient? http = null) { _settings = (settings ?? new ProviderSettings()).Normalized(); _http = http ?? ProviderHttp.Default; }

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) return new TranslationResult(string.Empty, sourceLang, Name);
        var key = ProviderHttp.RequireApiKey(_settings.ApiKey, Name); var folder = ProviderHttp.RequireRegion(_settings.Region, Name, "folder ID");
        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "yandexgpt-lite/latest" : _settings.Model;
        var payload = new { modelUri = "gpt://" + folder + "/" + model, completionOptions = new { stream = false, temperature = 0.2, maxTokens = "2000" }, messages = new[] { new { role = "system", text = FfxivTranslationPrompt.Build(sourceLang, targetLang) }, new { role = "user", text } } };
        var payloadText = JsonSerializer.Serialize(payload);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(payloadText, Encoding.UTF8, "application/json") };
            request.Headers.TryAddWithoutValidation("Authorization", "Api-Key " + key); request.Headers.TryAddWithoutValidation("x-folder-id", folder);
            try
            {
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false); var body = await ProviderHttp.ReadAsync(response, ct).ConfigureAwait(false); var status = (int)response.StatusCode;
                if (status == 429) throw new TranslationException($"{Name} quota exceeded (HTTP {status}).");
                if (!response.IsSuccessStatusCode) { if (IsTransient(status) && attempt < 3) { await DelayAsync(attempt, ct); continue; } throw ProviderHttp.HttpError(Name, response, body); }
                var parsed = ParseContent(body); if (!string.IsNullOrWhiteSpace(parsed)) return new TranslationResult(parsed, sourceLang, Name);
                if (attempt < 3) { await DelayAsync(attempt, ct); continue; }
                throw new TranslationException($"{Name} returned an empty or malformed translation.");
            }
            catch (TranslationException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                if (attempt >= 3) throw ProviderHttp.Network(Name, ex);
                await DelayAsync(attempt, ct);
            }
            catch (HttpRequestException ex) when (attempt >= 3) { throw ProviderHttp.Network(Name, ex); }
            catch (Exception ex) when (attempt < 3 && (ex is HttpRequestException or TaskCanceledException or IOException)) { await DelayAsync(attempt, ct); }
        }
        throw new TranslationException($"{Name} returned an empty or malformed translation.");
    }
    internal static string ParseContent(string body)
    {
        try { using var doc = JsonDocument.Parse(body); var result = doc.RootElement.GetProperty("result"); var alternatives = result.GetProperty("alternatives"); var message = alternatives[0].GetProperty("message"); return AiResponseSanitizer.Strip(ProviderHttp.JsonText(message, "text")); }
        catch (Exception) { return string.Empty; }
    }
    private static bool IsTransient(int status) => status is 408 or 425 or 429 or >= 500 and <= 599;
    private static Task DelayAsync(int attempt, CancellationToken ct) => Task.Delay(600 * (1 << (attempt - 1)), ct);
}
