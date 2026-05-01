using Sharlayan.Core.Scanning;

namespace KrileHelper.Tests.Sharlayan;

public sealed class PointerResolverTests
{
    [Fact]
    public void Resolve_ReturnsSignatureAddressWhenNoPointerPath()
    {
        var sig = new Signature("TEST", "AA") { SigScanAddress = 0x1234 };

        var resolved = PointerResolver.Resolve(new FakeNativeMemory(), pid: 1, sig);

        Assert.Equal(0x1234UL, resolved);
    }

    [Fact]
    public void Resolve_FollowsPointerPathAndReturnsLastDereferenceAddress()
    {
        var mem = new FakeNativeMemory();
        mem.WriteUInt64(0x1010, 0x2000);
        mem.WriteUInt64(0x2020, 0x3000);
        var sig = new Signature("TEST", "AA", new long[] { 0x10, 0x20 }) { SigScanAddress = 0x1000 };

        var resolved = PointerResolver.Resolve(mem, pid: 1, sig);

        Assert.Equal(0x2020UL, resolved);
    }

    [Fact]
    public void Resolve_HandlesAsmRelativeDisplacementForFirstHop()
    {
        var mem = new FakeNativeMemory();
        mem.WriteInt32(0x1004, 0x20);
        var sig = new Signature("TEST", "AA", new long[] { 0x04 }, isAsm: true) { SigScanAddress = 0x1000 };

        var resolved = PointerResolver.Resolve(mem, pid: 1, sig);

        Assert.Equal(0x1004UL, resolved);
    }
}
