namespace KrileHelper.UI.Services;

public sealed record LanguageOption(string Code, string Display)
{
    public override string ToString() => $"{Display} ({Code})";
}

public static class LanguageOptions
{
    public static IReadOnlyList<LanguageOption> Targets { get; } = new[]
    {
        new LanguageOption("es", "Español"),
        new LanguageOption("en", "English"),
        new LanguageOption("ja", "日本語"),
        new LanguageOption("zh-CN", "中文 (简体)"),
        new LanguageOption("zh-TW", "中文 (繁體)"),
        new LanguageOption("ko", "한국어"),
        new LanguageOption("de", "Deutsch"),
        new LanguageOption("fr", "Français"),
        new LanguageOption("pt", "Português"),
        new LanguageOption("it", "Italiano"),
        new LanguageOption("ru", "Русский"),
        new LanguageOption("nl", "Nederlands"),
        new LanguageOption("pl", "Polski"),
        new LanguageOption("tr", "Türkçe"),
        new LanguageOption("ar", "العربية"),
        new LanguageOption("hi", "हिन्दी"),
        new LanguageOption("vi", "Tiếng Việt"),
        new LanguageOption("th", "ภาษาไทย"),
        new LanguageOption("uk", "Українська"),
        new LanguageOption("ca", "Català"),
    };

    public static IReadOnlyList<LanguageOption> Sources { get; } = new[] { new LanguageOption("auto", "Auto-detect") }
        .Concat(Targets)
        .ToList();
}
