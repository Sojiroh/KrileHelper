namespace Sharlayan.Core.Native;

public interface INativeMemory
{
    int Read(int pid, ulong remoteAddress, Span<byte> destination);
}

public static class NativeMemoryExtensions
{
    public static byte[] ReadBytes(this INativeMemory mem, int pid, ulong remoteAddress, int length)
    {
        var buf = new byte[length];
        var n = mem.Read(pid, remoteAddress, buf);
        if (n != length) Array.Resize(ref buf, n);
        return buf;
    }

    public static uint ReadUInt32(this INativeMemory mem, int pid, ulong remoteAddress)
    {
        Span<byte> tmp = stackalloc byte[4];
        if (mem.Read(pid, remoteAddress, tmp) != 4) return 0;
        return BitConverter.ToUInt32(tmp);
    }

    public static ulong ReadUInt64(this INativeMemory mem, int pid, ulong remoteAddress)
    {
        Span<byte> tmp = stackalloc byte[8];
        if (mem.Read(pid, remoteAddress, tmp) != 8) return 0;
        return BitConverter.ToUInt64(tmp);
    }

    public static int ReadInt32(this INativeMemory mem, int pid, ulong remoteAddress)
    {
        Span<byte> tmp = stackalloc byte[4];
        if (mem.Read(pid, remoteAddress, tmp) != 4) return 0;
        return BitConverter.ToInt32(tmp);
    }
}
