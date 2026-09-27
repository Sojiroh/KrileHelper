using Microsoft.Data.Sqlite;
using Translation.Core;
using Xunit;
using Translation.Core.Reference;
using System.Security;

namespace KrileHelper.Tests.Translation;

public sealed class ReferenceTranslationServiceTests
{
    [Fact]
    public void LookupUsesPlayerNameGenderAndSpeakerPatterns()
    {
        var path = Path.Combine(Path.GetTempPath(), "KrileReference-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            CreateIndex(path);

            using var service = new ReferenceTranslationService(path);

            Assert.True(service.TryTranslate(
                "Welcome, D'ark!", "en", "ru", "D'ark One", true, out var addressed));
            Assert.Equal("Добро пожаловать, D'ark!", addressed);

            Assert.True(service.TryTranslate(
                "The position is yours, D'ark?", "en", "ru", "D'ark One", false, out var gendered));
            Assert.Equal("Позиция твоя, D'ark?", gendered);

            Assert.True(service.TryTranslateSpeaker(
                "Mother Miounne", "en", "ru", out var speaker));
            Assert.Equal("Матушка Миунна", speaker);

            Assert.True(service.TryTranslate(
                "What will you say?\n1. I thought you were always watching?\n2. Miss that part, did you?",
                "en", "ru", null, null, out var choices));
            Assert.Equal(
                "Что ты скажешь?\n1. Я думал, ты всегда наблюдаешь?\n2. Пропустил эту часть, да?",
                choices);

            Assert.False(service.TryTranslate(
                "What will you say?\n1. I thought you were always watching?\n2. Unknown answer",
                "en", "ru", null, null, out var incomplete));
            Assert.Empty(incomplete);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void UnsupportedLanguageOrTargetDoesNotClaimReferenceSuccess()
    {
        var path = Path.Combine(Path.GetTempPath(), "KrileReference-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            CreateIndex(path);
            using var service = new ReferenceTranslationService(path);

            Assert.False(service.TryTranslate("Known", "ko", "ru", null, null, out var unsupportedSource));
            Assert.Empty(unsupportedSource);
            Assert.False(service.TryTranslate("Known", "en", "de", null, null, out var unsupportedTarget));
            Assert.Empty(unsupportedTarget);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }


    [Fact]
    public void BuilderIndexesEachRenderedChoice()
    {
        var builder = new ReferenceIndexBuilder();
        builder.AddSheet(
            "exd/Quest/001",
            Sheet("1", "The sky is <var 08 E901 ((clear)) ((cloudy)) /var>."),
            Sheet("1", "Небо <var 08 E901 ((ясное)) ((пасмурное)) /var>."));

        Assert.Equal("Небо ясное.", builder.Lines["The sky is clear."]);
        Assert.Equal("Небо пасмурное.", builder.Lines["The sky is cloudy."]);
    }

    [Fact]
    public void CancelledIndexWriteLeavesExistingDatabaseInPlace()
    {
        var path = Path.Combine(Path.GetTempPath(), "KrileReference-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            CreateIndex(path);
            var replacement = new ReferenceIndexBuilder();
            replacement.AddSheet(
                "exd/Quest/001",
                Sheet("1", "A replacement line."),
                Sheet("1", "Новая строка."));

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                ReferenceIndexUpdater.WriteAndInstall(
                    path, replacement, "en", "ru", "new", "test", null, cancellation.Token));

            using var source = new SqliteReferenceTranslationSource(path);
            Assert.True(source.TryGetTranslation("What will you say?", out var translation));
            Assert.Equal("Что ты скажешь?", translation);
            Assert.False(File.Exists(path + ".new"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
            try { File.Delete(path + ".new"); } catch (IOException) { }
        }
    }

    private static string Sheet(string id, string text) =>
        "<xliff><file><body><trans-unit id=\"" + id + "\"><source>" + id +
        "</source><target state=\"final\">" + SecurityElement.Escape(text) +
        "</target></trans-unit></body></file></xliff>";
    private static void CreateIndex(string path)
    {
        using var connection = new SqliteConnection("Data Source=" + path);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE line (source TEXT PRIMARY KEY, translated TEXT NOT NULL) WITHOUT ROWID;
            CREATE TABLE pattern (source TEXT PRIMARY KEY, translated TEXT NOT NULL) WITHOUT ROWID;
            CREATE TABLE speaker (source TEXT PRIMARY KEY, translated TEXT NOT NULL) WITHOUT ROWID;
            CREATE TABLE item_pattern (source TEXT PRIMARY KEY, translated TEXT NOT NULL) WITHOUT ROWID;
            CREATE TABLE gendered (source TEXT NOT NULL, feminine INTEGER NOT NULL, translated TEXT NOT NULL, PRIMARY KEY (feminine, source)) WITHOUT ROWID;
            CREATE TABLE gendered_pattern (source TEXT NOT NULL, feminine INTEGER NOT NULL, translated TEXT NOT NULL, PRIMARY KEY (feminine, source)) WITHOUT ROWID;
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO pattern VALUES ('Welcome, ' || char(1) || '!', 'Добро пожаловать, ' || char(1) || '!');
            INSERT INTO line VALUES
                ('What will you say?', 'Что ты скажешь?'),
                ('I thought you were always watching?', 'Я думал, ты всегда наблюдаешь?'),
                ('Miss that part, did you?', 'Пропустил эту часть, да?');
            INSERT INTO gendered_pattern VALUES ('The position is yours, ' || char(1) || '?', 0, 'Позиция твоя, ' || char(1) || '?');
            INSERT INTO speaker VALUES ('Mother Miounne', 'Матушка Миунна');
            INSERT INTO meta VALUES ('language', 'ru'), ('sourceLanguage', 'en'), ('revision', 'test'), ('rules', '7'), ('lines', '0');
            """;
        command.ExecuteNonQuery();
    }
}
