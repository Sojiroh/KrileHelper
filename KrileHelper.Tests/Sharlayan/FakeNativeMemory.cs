using Sharlayan.Core.Native;

namespace KrileHelper.Tests.Sharlayan;

internal sealed class FakeNativeMemory : INativeMemory
{
    private readonly SortedDictionary<ulong, byte[]> _blocks = new();
    private readonly Dictionary<ulong, int> _shortReads = new();

    public void Write(ulong address, byte[] bytes) => _blocks[address] = bytes.ToArray();

    public void WriteUInt32(ulong address, uint value) => Write(address, BitConverter.GetBytes(value));

    public void WriteInt32(ulong address, int value) => Write(address, BitConverter.GetBytes(value));

    public void WriteUInt64(ulong address, ulong value) => Write(address, BitConverter.GetBytes(value));

    public void ShortReadAt(ulong address, int bytesToRead) => _shortReads[address] = bytesToRead;

    public int Read(int pid, ulong remoteAddress, Span<byte> destination)
    {
        if (_shortReads.TryGetValue(remoteAddress, out var shortRead))
            destination = destination[..Math.Min(shortRead, destination.Length)];

        var copied = 0;
        while (copied < destination.Length)
        {
            if (!TryReadByte(remoteAddress + (ulong)copied, out var value))
                break;

            destination[copied++] = value;
        }

        return copied;
    }

    private bool TryReadByte(ulong address, out byte value)
    {
        foreach (var (start, block) in _blocks)
        {
            var end = start + (ulong)block.Length;
            if (address < start || address >= end)
                continue;

            value = block[address - start];
            return true;
        }

        value = 0;
        return false;
    }
}
