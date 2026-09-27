using System.Text.Json;
using Translation.Core;

namespace KrileHelper.Tests.Translation;

public sealed class ProviderCatalogAndCacheTests
{
    [Fact]
    public async Task FactoryDoesNotSilentlyReplaceMissingConfiguration()
    {
        var translator = TranslationEngines.Create("DeepL", new ProviderSettings());
        await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("hello", "en", "es"));
    }

    [Fact]
    public async Task DeepLRefusalStopsSubsequentNetworkRequestsDuringCooldown()
    {
        int requests = 0;
        using var http = new HttpClient(new FakeHttpMessageHandler(_ =>
        {
            requests++;
            return FakeHttpMessageHandler.Json("{}", System.Net.HttpStatusCode.TooManyRequests);
        }));
        var translator = new DeepLFreeTranslator(http);
        await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("First line", "en", "es"));
        await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("Next line", "en", "es"));
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task CacheSeparatesLanguagePairsAndEvictsLeastRecentlyUsedEntry()
    {
        var inner = new CountingTranslator("config-a");
        var cached = new CachedTranslator(inner, capacity: 2);

        await cached.TranslateAsync("one", "en", "es");
        await cached.TranslateAsync("one", "en", "es");
        await cached.TranslateAsync("one", "ja", "es");
        await cached.TranslateAsync("two", "en", "es");
        await cached.TranslateAsync("one", "en", "es");

        Assert.Equal(4, inner.Calls);
    }

    [Fact]
    public async Task CacheHitStillHonorsCallerCancellation()
    {
        var cached = new CachedTranslator(new CountingTranslator("config-a"));
        await cached.TranslateAsync("one", "en", "es");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cached.TranslateAsync("one", "en", "es", cancellation.Token));
    }

    [Fact]
    public async Task GoogleMalformedResponseRaisesTranslationException()
    {
        using var http = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("not-json")));
        var translator = new GoogleFreeTranslator(http);

        await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("hello", "en", "es"));
    }

    [Fact]
    public async Task GoogleCancellationIsNotConvertedToProviderError()
    {
        using var http = new HttpClient(new CancelingHttpMessageHandler());
        var translator = new GoogleFreeTranslator(http);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            translator.TranslateAsync("hello", "en", "es", cancellation.Token));
    }

    [Fact]
    public async Task LocalEndpointWithoutPathGetsOpenAiChatCompletionsSuffix()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(
            "{\"choices\":[{\"message\":{\"content\":\"hola\"}}]}"));
        using var http = new HttpClient(handler);
        var translator = new OllamaTranslator(
            new ProviderSettings { Endpoint = "http://localhost:11434", Model = "llama3.1" }, http);

        var result = await translator.TranslateAsync("hello", "en", "es");

        Assert.Equal("hola", result.TranslatedText);
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task DeepLFreeRequestTimestampMatchesProtocolFingerprint()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(
            "{\"result\":{\"texts\":[{\"text\":\"hola\"}]}}"));
        using var http = new HttpClient(handler);
        var translator = new DeepLFreeTranslator(http);

        var result = await translator.TranslateAsync("limit", "en", "es");

        using var request = JsonDocument.Parse(handler.LastRequestBody!);
        var timestamp = request.RootElement.GetProperty("params").GetProperty("timestamp").GetInt64();
        Assert.Equal(0, timestamp % 3);
        Assert.Equal("hola", result.TranslatedText);
    }

    [Fact]
    public async Task GeminiKeepsSuccessfulTextContainingQuotaIdentifier()
    {
        using var http = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(
            "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"RESOURCE_EXHAUSTED is a literal phrase\"}]}}]}")));
        var translator = new GeminiTranslator(new ProviderSettings { ApiKey = "test-key" }, http);

        var result = await translator.TranslateAsync("hello", "en", "es");

        Assert.Equal("RESOURCE_EXHAUSTED is a literal phrase", result.TranslatedText);
    }

    [Fact]
    public async Task ClaudeKeepsSuccessfulTextContainingRateLimitIdentifier()
    {
        using var http = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(
            "{\"content\":[{\"type\":\"text\",\"text\":\"rate_limit_error is a literal phrase\"}]}")));
        var translator = new ClaudeTranslator(new ProviderSettings { ApiKey = "test-key" }, http);

        var result = await translator.TranslateAsync("hello", "en", "es");

        Assert.Equal("rate_limit_error is a literal phrase", result.TranslatedText);
    }

    [Fact]
    public async Task OpenAiMalformedSuccessfulResponseRaisesTranslationException()
    {
        using var http = new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}")));
        var translator = new OpenAITranslator(new ProviderSettings { ApiKey = "test-key" }, http);

        await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("hello", "en", "es"));
    }

    [Fact]
    public async Task ClaudeFinalTimeoutBecomesTranslationException()
    {
        using var http = new HttpClient(new TimeoutHttpMessageHandler());
        var translator = new ClaudeTranslator(new ProviderSettings { ApiKey = "test-key" }, http);

        await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("hello", "en", "es"));
    }

    [Fact]
    public async Task YandexGptFinalTimeoutBecomesTranslationException()
    {
        using var http = new HttpClient(new TimeoutHttpMessageHandler());
        var translator = new YandexGptTranslator(new ProviderSettings { ApiKey = "test-key", Region = "folder" }, http);

        await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("hello", "en", "es"));
    }


    private sealed class TimeoutHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("simulated timeout"));
    }

    private sealed class CancelingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }

    private sealed class CountingTranslator : ITranslator
    {
        private readonly string _scope;
        public CountingTranslator(string scope) => _scope = scope;
        public int Calls { get; private set; }
        public string Name => "counting";
        public string CacheScope => _scope;
        public Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new TranslationResult($"{text}:{sourceLang}:{targetLang}", sourceLang, Name));
        }
    }
}
