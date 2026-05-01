using Sharlayan.Core.Scanning;

namespace KrileHelper.Tests.Sharlayan;

public sealed class SignatureTests
{
    [Fact]
    public void ParsePattern_ParsesSpacedHexAndWildcards()
    {
        var pattern = Signature.ParsePattern("48 8B ?? ** 90");

        Assert.Equal(new short[] { 0x48, 0x8B, -1, -1, 0x90 }, pattern);
    }

    [Fact]
    public void ParsePattern_ParsesCompactHexAndWildcardNibbles()
    {
        var pattern = Signature.ParsePattern("488B??*090");

        Assert.Equal(new short[] { 0x48, 0x8B, -1, -1, 0x90 }, pattern);
    }

    [Fact]
    public void ParsePattern_RejectsOddLengthCompactPattern()
    {
        Assert.Throws<ArgumentException>(() => Signature.ParsePattern("ABC"));
    }

    [Fact]
    public void ParsePattern_RejectsBadHexCharacters()
    {
        Assert.Throws<FormatException>(() => Signature.ParsePattern("48ZZ"));
    }
}
