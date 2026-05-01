using Sharlayan.Core.Scanning;

namespace KrileHelper.Tests.Sharlayan;

public sealed class SignatureScannerTests
{
    [Fact]
    public void FindInBuffer_FindsPatternWithWildcard()
    {
        var haystack = new byte[] { 0x00, 0x48, 0x8B, 0xAB, 0x90, 0xFF };
        var sig = new Signature("TEST", "48 8B ?? 90");

        var hit = SignatureScanner.FindInBuffer(haystack, haystack.Length, sig);

        Assert.Equal(1, hit);
    }

    [Fact]
    public void FindInBuffer_FindsPatternAtEndBoundary()
    {
        var haystack = new byte[] { 0x01, 0x02, 0xAA, 0xBB };
        var sig = new Signature("TEST", "AA BB");

        var hit = SignatureScanner.FindInBuffer(haystack, haystack.Length, sig);

        Assert.Equal(2, hit);
    }

    [Fact]
    public void FindInBuffer_RespectsStartOffset()
    {
        var haystack = new byte[] { 0xAA, 0xBB, 0xAA, 0xBB };
        var sig = new Signature("TEST", "AA BB");

        var hit = SignatureScanner.FindInBuffer(haystack, haystack.Length, sig, start: 1);

        Assert.Equal(2, hit);
    }

    [Fact]
    public void FindInBuffer_ReturnsMinusOneWhenAbsent()
    {
        var haystack = new byte[] { 0xAA, 0xBC, 0xCC };
        var sig = new Signature("TEST", "AA BB");

        var hit = SignatureScanner.FindInBuffer(haystack, haystack.Length, sig);

        Assert.Equal(-1, hit);
    }
}
