using System.Net;
using Translation.Core;

namespace KrileHelper.Tests.Translation;

public sealed class GoogleFreeTranslatorTests
{
    [Fact]
    public async Task TranslateAsync_ParsesTranslatedChunksAndDetectedLanguage()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(
            "[[[\"hola \",\"hello \",null,null,1],[\"mundo\",\"world\",null,null,1]],null,\"en\"]"));
        var translator = new GoogleFreeTranslator(new HttpClient(handler));

        var result = await translator.TranslateAsync("hello world", "auto", "es");

        Assert.Equal("hola mundo", result.TranslatedText);
        Assert.Equal("en", result.DetectedSourceLang);
        Assert.Equal("GoogleFree", result.EngineName);
        Assert.Contains("sl=auto", handler.LastRequest!.RequestUri!.Query);
        Assert.Contains("tl=es", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsTranslationExceptionForHttpErrors()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("rate limit", HttpStatusCode.TooManyRequests));
        var translator = new GoogleFreeTranslator(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("hello", "en", "es"));

        Assert.Contains("Google returned 429", ex.Message);
    }
}
