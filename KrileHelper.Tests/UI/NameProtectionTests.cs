using KrileHelper.UI.Services;
using Translation.Core;

namespace KrileHelper.Tests.UI;

public sealed class NameProtectionTests
{
    [Fact]
    public void ProtectsFullNameAndForenameWithoutChangingSubstrings()
    {
        var protectedText = PlayerNameProtection.Hide("Also, Al Smith, tell Al to return.", "Al Smith");
        Assert.DoesNotContain("Al Smith", protectedText.Text);
        Assert.DoesNotContain(" Al ", protectedText.Text);
        Assert.StartsWith("Also,", protectedText.Text);
        Assert.Equal("También, Al Smith, dile a Al que vuelva.",
            protectedText.Restore("También, \uE800, dile a \uE801 que vuelva."));
    }

    [Fact]
    public void ExistingPrivateUseCharacterIsNotRestoredAsPlayerName()
    {
        var protectedText = PlayerNameProtection.Hide("\uE800 greets Al Smith.", "Al Smith");
        Assert.Equal("\uE800 greets Al Smith.", protectedText.Restore(protectedText.Text));
    }

    [Fact]
    public void RejectsTranslationThatDroppedProtectedName()
    {
        var protectedText = PlayerNameProtection.Hide("Hello, Al Smith!", "Al Smith");
        Assert.Throws<TranslationException>(() => protectedText.Restore("¡Hola!"));
    }

    [Theory]
    [InlineData("Player Name：Hello", "001B", "Player Name", "Hello")]
    [InlineData("Cid: Understood.", "003D", "Cid", "Understood.")]
    [InlineData("Mentor symbols are as follows: combat", "0039", "", "Mentor symbols are as follows: combat")]
    public void SplitsSpeakersOnlyOnNamedChannels(string line, string code, string speaker, string body)
    {
        Assert.Equal((speaker, body), ChatTranslationService.SplitSpeaker(line, code));
    }
}
