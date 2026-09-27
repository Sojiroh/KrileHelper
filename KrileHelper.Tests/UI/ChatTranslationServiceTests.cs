using KrileHelper.UI.Services;
using Translation.Core;
using Translation.Core.Reference;

namespace KrileHelper.Tests.UI;

public sealed class ChatTranslationServiceTests
{
    [Fact]
    public async Task MixedChoiceUsesReferenceLinesAndSendsOnlyMissingAnswerToEngine()
    {
        var root = Path.Combine(Path.GetTempPath(), "krile-choice-" + Guid.NewGuid().ToString("N"));
        var sheet = Path.Combine(root, "exd", "Choice");
        Directory.CreateDirectory(sheet);
        try
        {
            File.WriteAllText(Path.Combine(sheet, "en.xlf"), """
                <xliff><file><body>
                <trans-unit id="1"><source>1</source><target state="final">Question?</target></trans-unit>
                <trans-unit id="2"><source>2</source><target state="final">Known.</target></trans-unit>
                </body></file></xliff>
                """);
            File.WriteAllText(Path.Combine(sheet, "ru.xlf"), """
                <xliff><file><body>
                <trans-unit id="1"><source>1</source><target state="final">Вопрос?</target></trans-unit>
                <trans-unit id="2"><source>2</source><target state="final">Известно.</target></trans-unit>
                </body></file></xliff>
                """);
            using var reference = new ReferenceTranslationService(Path.Combine(root, "reference.sqlite"));
            Assert.Equal(ReferenceUpdateOutcome.Updated, reference.BuildFromFolder("en", root).Outcome);
            var service = new ChatTranslationService(reference);
            var engine = new MissingAnswerTranslator();
            var context = new ChatTranslationContext("en", "ru", false, false, true, "en", "", null, Array.Empty<string>());
            var result = await service.TranslateAsync(engine, "Question?\n1. Known.\n2. Unknown.", "003D", context,
                explicitSpeaker: "");
            Assert.Equal("Вопрос?\n1. Известно.\n2. Неизвестно.", result.Text);
            Assert.Equal(new[] { "Unknown." }, engine.Requests);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class MissingAnswerTranslator : ITranslator
    {
        public string Name => "MissingAnswer";
        public List<string> Requests { get; } = [];
        public Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct = default)
        {
            Requests.Add(text);
            if (text != "Unknown.") throw new InvalidOperationException("Reference text must not be sent to the engine.");
            return Task.FromResult(new TranslationResult("Неизвестно.", "en", Name));
        }
    }
}
