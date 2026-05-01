using Sharlayan.Core.ChatLog;
using Sharlayan.Core.Resources;
using Sharlayan.Core.Scanning;

namespace KrileHelper.Tests.Sharlayan;

public sealed class ChatLogReaderTests
{
    private const int Pid = 42;
    private const ulong PointerMap = 0x1000;
    private const ulong OffsetArray = 0x2000;
    private const ulong LogStart = 0x3000;

    [Fact]
    public void Poll_FirstRunSnapshotsThenReturnsOnlyNewEntries()
    {
        var mem = new FakeNativeMemory();
        var entry1 = ChatEntryTests.BuildRawEntry("000A", "Xold");
        var entry2 = ChatEntryTests.BuildRawEntry("000A", "Xnew");
        ConfigureReaderMemory(mem, currentArrayIndex: 1, entry1);
        var reader = CreateReader(mem);

        Assert.Empty(reader.Poll());

        ConfigureReaderMemory(mem, currentArrayIndex: 2, entry1, entry2);
        var items = reader.Poll();

        var item = Assert.Single(items);
        Assert.Equal("new", item.Line);
    }

    [Fact]
    public void Poll_InvalidCurrentArrayIndexReturnsEmpty()
    {
        var mem = new FakeNativeMemory();
        ConfigurePointerMap(mem, currentArrayIndex: 1001, logLength: 64);
        mem.Write(OffsetArray, new byte[1000 * 4]);
        mem.Write(LogStart, new byte[64]);
        var reader = CreateReader(mem);

        var items = reader.Poll();

        Assert.Empty(items);
    }

    [Fact]
    public void Poll_ShortOffsetArrayReadReturnsEmpty()
    {
        var mem = new FakeNativeMemory();
        ConfigurePointerMap(mem, currentArrayIndex: 1, logLength: 64);
        mem.Write(OffsetArray, new byte[4]);
        var reader = CreateReader(mem);

        var items = reader.Poll();

        Assert.Empty(items);
    }

    [Fact]
    public void Poll_OversizedLogBufferReturnsEmptyWithoutAdvancingSnapshot()
    {
        var mem = new FakeNativeMemory();
        var entry1 = ChatEntryTests.BuildRawEntry("000A", "Xold");
        var entry2 = ChatEntryTests.BuildRawEntry("000A", "Xnew");
        ConfigureReaderMemory(mem, currentArrayIndex: 1, entry1);
        var reader = CreateReader(mem);
        Assert.Empty(reader.Poll());

        ConfigurePointerMap(mem, currentArrayIndex: 2, logLength: 17 * 1024 * 1024);
        WriteOffsets(mem, entry1.Length, entry1.Length + entry2.Length);
        Assert.Empty(reader.Poll());

        ConfigureReaderMemory(mem, currentArrayIndex: 2, entry1, entry2);
        var item = Assert.Single(reader.Poll());
        Assert.Equal("new", item.Line);
    }

    private static ChatLogReader CreateReader(FakeNativeMemory mem)
    {
        var sig = new Signature("CHATLOG", "AA") { SigScanAddress = PointerMap };
        var layout = new ChatLogPointersStruct
        {
            OffsetArrayStart = 0,
            OffsetArrayPos = 8,
            LogStart = 16,
            LogNext = 24,
        };
        return new ChatLogReader(mem, Pid, sig, layout);
    }

    private static void ConfigureReaderMemory(FakeNativeMemory mem, int currentArrayIndex, params byte[][] entries)
    {
        var log = entries.SelectMany(e => e).ToArray();
        ConfigurePointerMap(mem, currentArrayIndex, log.Length);
        WriteOffsets(mem, entries.Select((entry, index) => entries.Take(index + 1).Sum(e => e.Length)).ToArray());
        mem.Write(LogStart, log);
    }

    private static void ConfigurePointerMap(FakeNativeMemory mem, int currentArrayIndex, int logLength)
    {
        mem.WriteUInt64(PointerMap, OffsetArray);
        mem.WriteUInt64(PointerMap + 8, OffsetArray + (ulong)(currentArrayIndex * 4));
        mem.WriteUInt64(PointerMap + 16, LogStart);
        mem.WriteUInt64(PointerMap + 24, LogStart + (ulong)logLength);
    }

    private static void WriteOffsets(FakeNativeMemory mem, params int[] offsets)
    {
        var bytes = new byte[1000 * 4];
        for (var i = 0; i < offsets.Length; i++)
            BitConverter.GetBytes(offsets[i]).CopyTo(bytes, i * 4);
        mem.Write(OffsetArray, bytes);
    }
}
