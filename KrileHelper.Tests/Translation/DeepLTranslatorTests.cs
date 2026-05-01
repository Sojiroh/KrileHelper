using System.Net;
using Translation.Core;

namespace KrileHelper.Tests.Translation;

public sealed class DeepLTranslatorTests
{
    [Fact]
    public async Task TranslateAsync_MapsLanguagesAndParsesResponse()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(
            "{\"translations\":[{\"detected_source_language\":\"EN\",\"text\":\"hola\"}]}"));
        var translator = new DeepLTranslator("test-key:fx", new HttpClient(handler));

        var result = await translator.TranslateAsync("hello", "auto", "es");

        Assert.Equal("hola", result.TranslatedText);
        Assert.Equal("en", result.DetectedSourceLang);
        Assert.Equal("DeepL Free", result.EngineName);
        Assert.Equal("https://api-free.deepl.com/v2/translate", handler.LastRequest!.RequestUri!.ToString());
        Assert.True(handler.LastRequest.Headers.TryGetValues("Authorization", out var auth));
        Assert.Equal("DeepL-Auth-Key test-key:fx", Assert.Single(auth));

        var form = handler.LastRequestBody!;
        Assert.Contains("text=hello", form);
        Assert.Contains("target_lang=ES", form);
        Assert.DoesNotContain("source_lang", form);
    }

    [Fact]
    public async Task TranslateAsync_IncludesMappedSourceWhenNotAuto()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(
            "{\"translations\":[{\"detected_source_language\":\"JA\",\"text\":\"hello\"}]}"));
        var translator = new DeepLTranslator("test-key", new HttpClient(handler));

        await translator.TranslateAsync("こんにちは", "ja", "en");

        var form = handler.LastRequestBody!;
        Assert.Contains("source_lang=JA", form);
        Assert.Contains("target_lang=EN-US", WebUtility.UrlDecode(form));
        Assert.Equal("https://api.deepl.com/v2/translate", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task TranslateAsync_ThrowsForUnsupportedTargetBeforeHttpRequest()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called"));
        var translator = new DeepLTranslator("test-key", new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("hello", "en", "ar"));

        Assert.Contains("does not support target language 'ar'", ex.Message);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task TranslateAsync_MapsQuotaErrorToHelpfulMessage()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("quota", (HttpStatusCode)456));
        var translator = new DeepLTranslator("test-key", new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("hello", "en", "es"));

        Assert.Contains("monthly character quota exceeded", ex.Message);
    }
}
