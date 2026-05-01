namespace Translation.Core;

public interface ITranslator
{
    string Name { get; }

    /// <param name="sourceLang">ISO 639-1 code, or "auto" for detection.</param>
    /// <param name="targetLang">ISO 639-1 code (e.g. "es", "en", "ja").</param>
    Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default);
}

public sealed record TranslationResult(string TranslatedText, string DetectedSourceLang, string EngineName);

public sealed class TranslationException : Exception
{
    public TranslationException(string message) : base(message) { }
    public TranslationException(string message, Exception inner) : base(message, inner) { }
}
