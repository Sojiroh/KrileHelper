using System.Net;
using System.Text;
using System.Text.Json;

namespace Translation.Core;

/// <summary>
/// DeepL translator. Detects Free vs Pro tier from the API key suffix
/// (Free keys end with ":fx").
/// </summary>
public sealed class DeepLTranslator : ITranslator
{
    private const string FreeEndpoint = "https://api-free.deepl.com/v2/translate";
    private const string ProEndpoint = "https://api.deepl.com/v2/translate";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _endpoint;

    public string Name { get; }

    public DeepLTranslator(string apiKey, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("DeepL API key is required.", nameof(apiKey));

        _apiKey = apiKey.Trim();
        bool isFree = _apiKey.EndsWith(":fx", StringComparison.OrdinalIgnoreCase);
        _endpoint = isFree ? FreeEndpoint : ProEndpoint;
        Name = isFree ? "DeepL Free" : "DeepL Pro";

        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult("", sourceLang, Name);

        string? deeplSource = LanguageMap.ToDeepLSource(sourceLang);
        string deeplTarget = LanguageMap.ToDeepLTarget(targetLang)
            ?? throw new TranslationException($"DeepL does not support target language '{targetLang}'.");

        var form = new List<KeyValuePair<string, string>>
        {
            new("text", text),
            new("target_lang", deeplTarget),
        };
        if (deeplSource is not null)
            form.Add(new("source_lang", deeplSource));

        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {_apiKey}");

        try
        {
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw await BuildHttpErrorAsync(resp, ct).ConfigureAwait(false);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return ParseResponse(stream, sourceLang);
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException("Network error contacting DeepL.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TranslationException("DeepL request timed out.", ex);
        }
    }

    private static async Task<TranslationException> BuildHttpErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        string body = "";
        try { body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); } catch { }
        var snippet = body.Length > 200 ? body[..200] + "…" : body;
        return (int)resp.StatusCode switch
        {
            401 or 403 => new TranslationException("DeepL: invalid API key (401/403). Check Settings → API key."),
            429 => new TranslationException("DeepL: rate-limited (429). Slow down requests."),
            456 => new TranslationException("DeepL: monthly character quota exceeded (456)."),
            _ => new TranslationException($"DeepL HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {snippet}"),
        };
    }

    private TranslationResult ParseResponse(Stream stream, string requestedSource)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (!root.TryGetProperty("translations", out var translations) || translations.ValueKind != JsonValueKind.Array)
            throw new TranslationException("Unexpected DeepL response shape.");

        var sb = new StringBuilder();
        string detected = requestedSource;
        foreach (var t in translations.EnumerateArray())
        {
            if (t.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                sb.Append(text.GetString());
            if (t.TryGetProperty("detected_source_language", out var dsl) && dsl.ValueKind == JsonValueKind.String)
                detected = dsl.GetString()?.ToLowerInvariant() ?? detected;
        }
        return new TranslationResult(sb.ToString(), detected, Name);
    }
}
