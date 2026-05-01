using System.Net;
using System.Text.Json;

namespace Translation.Core;

/// <summary>
/// Uses the unauthenticated translate.googleapis.com endpoint that returns JSON.
/// More robust than scraping the m.translate.google.com HTML, but Google may rate-limit
/// aggressive callers. Suitable for chat-rate translation (a few requests per second).
/// </summary>
public sealed class GoogleFreeTranslator : ITranslator
{
    private const string Endpoint = "https://translate.googleapis.com/translate_a/single";

    private readonly HttpClient _http;

    public string Name => "GoogleFree";

    public GoogleFreeTranslator(HttpClient? http = null)
    {
        _http = http ?? CreateDefaultClient();
    }

    private static HttpClient CreateDefaultClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        // Some Cloudflare-fronted regions reject the default UA.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) Gecko/20100101 Firefox/120.0");
        return c;
    }

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult("", sourceLang, Name);

        var url = $"{Endpoint}?client=gtx&dt=t&sl={WebUtility.UrlEncode(sourceLang)}&tl={WebUtility.UrlEncode(targetLang)}&q={WebUtility.UrlEncode(text)}";
        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new TranslationException($"Google returned {(int)resp.StatusCode} {resp.ReasonPhrase}");

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return ParseResponse(stream);
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException("Network error contacting Google.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TranslationException("Google translate request timed out.", ex);
        }
    }

    // Response shape:
    //   [ [ ["translated chunk","src chunk", null,null,N], ["chunk2","src2",...], ... ],
    //     null, "en", null, null, null, 1, [], [["en"],null,[1],["en"]] ]
    private TranslationResult ParseResponse(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 1)
            throw new TranslationException("Unexpected Google response shape.");

        var sb = new System.Text.StringBuilder();
        var chunks = root[0];
        if (chunks.ValueKind == JsonValueKind.Array)
        {
            foreach (var chunk in chunks.EnumerateArray())
            {
                if (chunk.ValueKind == JsonValueKind.Array && chunk.GetArrayLength() >= 1)
                {
                    var part = chunk[0];
                    if (part.ValueKind == JsonValueKind.String)
                        sb.Append(part.GetString());
                }
            }
        }

        string detected = "auto";
        if (root.GetArrayLength() >= 3 && root[2].ValueKind == JsonValueKind.String)
            detected = root[2].GetString() ?? "auto";

        return new TranslationResult(sb.ToString(), detected, Name);
    }
}
