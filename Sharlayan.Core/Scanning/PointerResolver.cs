using Sharlayan.Core.Native;

namespace Sharlayan.Core.Scanning;

internal static class PointerResolver
{
    public static ulong Resolve(INativeMemory mem, int pid, Signature sig)
    {
        ulong sigScanAddress = sig.SigScanAddress;
        var path = sig.PointerPath;
        bool asm = sig.IsAsmSignature;

        if (path is null || path.Count == 0)
            return sigScanAddress;

        ulong nextAddr = sigScanAddress;
        ulong baseAddr = sigScanAddress;
        bool isAsm = asm;
        Span<byte> tmp = stackalloc byte[8];

        foreach (var offset in path)
        {
            baseAddr = unchecked((ulong)((long)nextAddr + offset));
            if (baseAddr == 0) return 0;

            if (isAsm)
            {
                if (mem.Read(pid, baseAddr, tmp[..4]) != 4) return 0;
                int disp = BitConverter.ToInt32(tmp[..4]);
                nextAddr = unchecked((ulong)((long)baseAddr + disp + 4));
                isAsm = false;
            }
            else
            {
                if (mem.Read(pid, baseAddr, tmp) != 8) return 0;
                nextAddr = BitConverter.ToUInt64(tmp);
            }
        }

        return baseAddr;
    }
}
