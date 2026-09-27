using System.Text;
using System.Text.Json;

namespace Translation.Core;

/// <summary>Unauthenticated DeepL web translator.</summary>
public sealed class DeepLFreeTranslator : ITranslator
{
    private const string Endpoint = "https://www2.deepl.com/jsonrpc";
    private static long _requestId = Random.Shared.NextInt64(8_300_000_000L, 8_399_999_000L);
    private readonly HttpClient _http;
    private long _refusedUntil;
    public string Name => "DeepL Free";
    public string CacheScope => "DeepLFree";
    public DeepLFreeTranslator(HttpClient? http = null) => _http = http ?? ProviderHttp.Default;

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) return new TranslationResult(string.Empty, sourceLang, Name);
        ct.ThrowIfCancellationRequested();
        if (Environment.TickCount64 < Interlocked.Read(ref _refusedUntil))
            throw new TranslationException("DeepL Free asked for fewer requests; waiting one minute before contacting it again.");
        var source = ProviderHttp.NormalizeSource(sourceLang);
        var target = ProviderHttp.NormalizeTarget(targetLang).ToUpperInvariant();
        var id = Interlocked.Increment(ref _requestId);
        var body = BuildRequestBody(text, source, target, id,
            AdjustTimestamp(text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("x-app-os-name", "iOS");
        req.Headers.TryAddWithoutValidation("x-app-os-version", "16.3.0");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("x-app-build", "510265");
        req.Headers.TryAddWithoutValidation("x-app-version", "2.9.1");
        req.Headers.TryAddWithoutValidation("x-app-device", "iPhone13,2");
        req.Headers.TryAddWithoutValidation("User-Agent", "DeepL-iOS/2.9.1 iOS 16.3.0 (iPhone13,2)");
        try
        {
            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var responseBody = await ProviderHttp.ReadAsync(response, ct).ConfigureAwait(false);
            if ((int)response.StatusCode == 429)
            {
                Interlocked.Exchange(ref _refusedUntil, Environment.TickCount64 + 60_000);
                throw new TranslationException("DeepL Free is rate-limited (429); requests paused for one minute.");
            }
            if (!response.IsSuccessStatusCode)
                throw ProviderHttp.HttpError(Name, response, responseBody);
            return new TranslationResult(ParseTranslation(responseBody), sourceLang, Name);
        }
        catch (TranslationException) { throw; }
        catch (HttpRequestException ex) { throw ProviderHttp.Network(Name, ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw ProviderHttp.Network(Name, ex); }
    }

    internal static long AdjustTimestamp(string text, long now)
    {
        var count = text.Count(c => c == 'i');
        if (count == 0) return now;
        count++;
        return now - now % count + count;
    }

    internal static string BuildRequestBody(string text, string sourceLang, string targetLang, long id, long timestamp)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            method = "LMT_handle_texts",
            id,
            @params = new
            {
                texts = new[] { new { text = text ?? string.Empty, requestAlternatives = 0 } },
                splitting = "newlines",
                lang = new { source_lang_user_selected = sourceLang, target_lang = targetLang },
                timestamp,
                commonJobParams = new { wasSpoken = false, transcribe_as = string.Empty }
            }
        };
        var body = JsonSerializer.Serialize(payload);
        var method = (id + 5) % 29 == 0 || (id + 3) % 13 == 0 ? "\"method\" : \"" : "\"method\": \"";
        return body.Replace("\"method\":\"LMT_handle_texts\"", method + "LMT_handle_texts\"", StringComparison.Ordinal);
    }

    internal static string ParseTranslation(string body)
    {
        using var doc = ProviderHttp.ParseJson(body, "DeepL Free");
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("texts", out var texts) || texts.ValueKind != JsonValueKind.Array)
            return string.Empty;
        var sb = new StringBuilder();
        foreach (var item in texts.EnumerateArray())
            sb.Append(ProviderHttp.JsonText(item, "text"));
        return sb.ToString();
    }
}

public sealed class PapagoTranslator : ITranslator
{
    private const string Endpoint = "https://papago.naver.com/api/text/translation";
    private readonly HttpClient _http;
    public string Name => "Papago";
    public PapagoTranslator(HttpClient? http = null) => _http = http ?? ProviderHttp.Default;

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) return new TranslationResult(string.Empty, sourceLang, Name);
        var fields = new Dictionary<string, string>
        {
            ["source"] = string.IsNullOrWhiteSpace(sourceLang) ? "auto" : sourceLang,
            ["target"] = ProviderHttp.NormalizeTarget(targetLang), ["text"] = text,
            ["dict"] = "false", ["useGlossary"] = "false", ["honorific"] = "false"
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new FormUrlEncodedContent(fields) };
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/124 Safari/537.36");
        req.Headers.TryAddWithoutValidation("device-type", "pc");
        req.Headers.TryAddWithoutValidation("Origin", "https://papago.naver.com");
        req.Headers.TryAddWithoutValidation("Referer", "https://papago.naver.com/");
        try
        {
            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var body = await ProviderHttp.ReadAsync(response, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw ProviderHttp.HttpError(Name, response, body);
            using var doc = ProviderHttp.ParseJson(body, Name);
            var translated = ProviderHttp.JsonText(doc.RootElement, "translatedText");
            return new TranslationResult(translated, sourceLang, Name);
        }
        catch (TranslationException) { throw; }
        catch (HttpRequestException ex) { throw ProviderHttp.Network(Name, ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw ProviderHttp.Network(Name, ex); }
    }
}

public sealed class AzureTranslator : ITranslator
{
    private const string DefaultEndpoint = "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0";
    private readonly ProviderSettings _settings;
    private readonly HttpClient _http;
    public string Name => "Azure";
    public string CacheScope => $"{Name}:{_settings.ApiKey}:{_settings.Region}";
    public AzureTranslator(ProviderSettings settings, HttpClient? http = null)
    {
        _settings = (settings ?? new ProviderSettings()).Normalized(); _http = http ?? ProviderHttp.Default;
    }

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        var key = ProviderHttp.RequireApiKey(_settings.ApiKey, Name);
        if (string.IsNullOrEmpty(text)) return new TranslationResult(string.Empty, sourceLang, Name);
        var target = ProviderHttp.NormalizeTarget(targetLang);
        var source = ProviderHttp.NormalizeSource(sourceLang);
        var url = DefaultEndpoint + "&to=" + Uri.EscapeDataString(target);
        if (!source.Equals("auto", StringComparison.OrdinalIgnoreCase)) url += "&from=" + Uri.EscapeDataString(source);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(new[] { new { Text = text } }), Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", key);
        if (!string.IsNullOrWhiteSpace(_settings.Region)) req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", _settings.Region);
        try
        {
            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var body = await ProviderHttp.ReadAsync(response, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw ProviderHttp.HttpError(Name, response, body);
            using var doc = ProviderHttp.ParseJson(body, Name);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return new TranslationResult(string.Empty, sourceLang, Name);
            var first = doc.RootElement[0];
            if (first.ValueKind != JsonValueKind.Object || !first.TryGetProperty("translations", out var translations) || translations.ValueKind != JsonValueKind.Array || translations.GetArrayLength() == 0)
                return new TranslationResult(string.Empty, sourceLang, Name);
            return new TranslationResult(ProviderHttp.JsonText(translations[0], "text"), sourceLang, Name);
        }
        catch (TranslationException) { throw; }
        catch (HttpRequestException ex) { throw ProviderHttp.Network(Name, ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw ProviderHttp.Network(Name, ex); }
    }
}

public sealed class GoogleCloudTranslator : ITranslator
{
    private const string Endpoint = "https://translation.googleapis.com/language/translate/v2";
    private readonly ProviderSettings _settings;
    private readonly HttpClient _http;
    public string Name => "GoogleCloud";
    public string CacheScope => $"{Name}:{_settings.ApiKey}";
    public GoogleCloudTranslator(ProviderSettings settings, HttpClient? http = null)
    {
        _settings = (settings ?? new ProviderSettings()).Normalized(); _http = http ?? ProviderHttp.Default;
    }

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        var key = ProviderHttp.RequireApiKey(_settings.ApiKey, Name);
        if (string.IsNullOrEmpty(text)) return new TranslationResult(string.Empty, sourceLang, Name);
        var payload = new Dictionary<string, object?> { ["q"] = text, ["target"] = ProviderHttp.NormalizeTarget(targetLang), ["format"] = "text" };
        var source = ProviderHttp.NormalizeSource(sourceLang); if (!source.Equals("auto", StringComparison.OrdinalIgnoreCase)) payload["source"] = source;
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint + "?key=" + Uri.EscapeDataString(key))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        try
        {
            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var body = await ProviderHttp.ReadAsync(response, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw ProviderHttp.HttpError(Name, response, body);
            using var doc = ProviderHttp.ParseJson(body, Name);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("translations", out var translations) || translations.ValueKind != JsonValueKind.Array || translations.GetArrayLength() == 0)
                return new TranslationResult(string.Empty, sourceLang, Name);
            return new TranslationResult(ProviderHttp.JsonText(translations[0], "translatedText"), sourceLang, Name);
        }
        catch (TranslationException) { throw; }
        catch (HttpRequestException ex) { throw ProviderHttp.Network(Name, ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw ProviderHttp.Network(Name, ex); }
    }
}

public sealed class YandexFreeTranslator : ITranslator
{
    private const string Endpoint = "https://translate.yandex.net/api/v1/tr.json/translate";
    private readonly HttpClient _http;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    public string Name => "YandexFree";
    public YandexFreeTranslator(HttpClient? http = null) => _http = http ?? ProviderHttp.Default;

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return new TranslationResult(string.Empty, sourceLang, Name);
        var source = ProviderHttp.NormalizeSource(sourceLang).ToLowerInvariant();
        var target = ProviderHttp.NormalizeTarget(targetLang).ToLowerInvariant();
        if (source == target) return new TranslationResult(text, sourceLang, Name);
        var pair = source == "auto" ? target : source + "-" + target;
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint + "?id=" + _sessionId + "-0-0&srv=android")
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("text", text), new("lang", pair), new("options", "4") })
        };
        req.Headers.UserAgent.ParseAdd("Yandex Translate/21.11.2");
        try
        {
            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var body = await ProviderHttp.ReadAsync(response, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw ProviderHttp.HttpError(Name, response, body);
            using var doc = ProviderHttp.ParseJson(body, Name);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out var statusCode) || statusCode != 200 || !doc.RootElement.TryGetProperty("text", out var parts) || parts.ValueKind != JsonValueKind.Array)
                return new TranslationResult(string.Empty, sourceLang, Name);
            var sb = new StringBuilder(); foreach (var part in parts.EnumerateArray()) { if (sb.Length > 0) sb.Append(' '); if (part.ValueKind == JsonValueKind.String) sb.Append(part.GetString()); }
            return new TranslationResult(sb.ToString(), sourceLang, Name);
        }
        catch (TranslationException) { throw; }
        catch (HttpRequestException ex) { throw ProviderHttp.Network(Name, ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw ProviderHttp.Network(Name, ex); }
    }
}

public sealed class YandexCloudTranslator : ITranslator
{
    private const string Endpoint = "https://translate.api.cloud.yandex.net/translate/v2/translate";
    private readonly ProviderSettings _settings; private readonly HttpClient _http;
    public string Name => "YandexCloud";
    public string CacheScope => $"{Name}:{_settings.ApiKey}:{_settings.Region}";
    public YandexCloudTranslator(ProviderSettings settings, HttpClient? http = null) { _settings = (settings ?? new ProviderSettings()).Normalized(); _http = http ?? ProviderHttp.Default; }

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        var key = ProviderHttp.RequireApiKey(_settings.ApiKey, Name);
        var folder = ProviderHttp.RequireRegion(_settings.Region, Name, "folder ID");
        if (string.IsNullOrEmpty(text)) return new TranslationResult(string.Empty, sourceLang, Name);
        var source = ProviderHttp.NormalizeSource(sourceLang);
        var payload = new Dictionary<string, object?> { ["folderId"] = folder, ["targetLanguageCode"] = ProviderHttp.NormalizeTarget(targetLang), ["texts"] = new[] { text }, ["format"] = "PLAIN_TEXT" };
        if (!source.Equals("auto", StringComparison.OrdinalIgnoreCase)) payload["sourceLanguageCode"] = source;
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("Authorization", "Api-Key " + key);
        try
        {
            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var body = await ProviderHttp.ReadAsync(response, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw ProviderHttp.HttpError(Name, response, body);
            using var doc = ProviderHttp.ParseJson(body, Name);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("translations", out var translations) || translations.ValueKind != JsonValueKind.Array || translations.GetArrayLength() == 0) return new TranslationResult(string.Empty, sourceLang, Name);
            return new TranslationResult(ProviderHttp.JsonText(translations[0], "text"), sourceLang, Name);
        }
        catch (TranslationException) { throw; }
        catch (HttpRequestException ex) { throw ProviderHttp.Network(Name, ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw ProviderHttp.Network(Name, ex); }
    }
}

public sealed class LibreTranslateTranslator : ITranslator
{
    private readonly ProviderSettings _settings; private readonly HttpClient _http;
    public string Name => "LibreTranslate";
    public string CacheScope => $"{Name}:{_settings.ApiKey}:{_settings.Endpoint}";
    public LibreTranslateTranslator(ProviderSettings settings, HttpClient? http = null) { _settings = (settings ?? new ProviderSettings()).Normalized(); _http = http ?? ProviderHttp.Default; }

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) return new TranslationResult(string.Empty, sourceLang, Name);
        var fields = new Dictionary<string, string> { ["q"] = text, ["source"] = ProviderHttp.NormalizeSource(sourceLang), ["target"] = ProviderHttp.NormalizeTarget(targetLang), ["format"] = "text" };
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey)) fields["api_key"] = _settings.ApiKey;
        if (string.IsNullOrWhiteSpace(_settings.Endpoint))
            throw new TranslationException($"{Name} endpoint is required. Configure it in Settings.");
        var endpoint = ProviderHttp.ValidateEndpoint(ProviderHttp.Endpoint(_settings.Endpoint, string.Empty, "/translate"), Name);
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(fields) };
        try
        {
            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var body = await ProviderHttp.ReadAsync(response, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw ProviderHttp.HttpError(Name, response, body);
            using var doc = ProviderHttp.ParseJson(body, Name);
            return new TranslationResult(ProviderHttp.JsonText(doc.RootElement, "translatedText"), sourceLang, Name);
        }
        catch (TranslationException) { throw; }
        catch (HttpRequestException ex) { throw ProviderHttp.Network(Name, ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw ProviderHttp.Network(Name, ex); }
    }
}
