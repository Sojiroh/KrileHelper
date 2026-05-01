using Sharlayan.Core.Native;
using Sharlayan.Core.Resources;
using Sharlayan.Core.Scanning;

namespace Sharlayan.Core.ChatLog;

public sealed class ChatLogReader
{
    private const int RingSlots = 1000;
    private const int MaxLogBufferBytes = 16 * 1024 * 1024;

    private readonly INativeMemory _mem;
    private readonly int _pid;
    private readonly Signature _chatSig;
    private readonly ChatLogPointersStruct _layout;

    private bool _firstRun = true;
    private int _previousArrayIndex;
    private int _previousOffset;

    internal ChatLogReader(INativeMemory mem, int pid, Signature chatSig, ChatLogPointersStruct layout)
    {
        _mem = mem;
        _pid = pid;
        _chatSig = chatSig;
        _layout = layout;
    }

    /// <summary>Resets state so the next Poll() returns historical entries again.</summary>
    public void Rewind()
    {
        _firstRun = true;
        _previousArrayIndex = 0;
        _previousOffset = 0;
    }

    /// <summary>
    /// Returns chat entries written since the previous Poll() call. The first call
    /// returns no entries (it just snapshots the current ring head); subsequent
    /// calls return everything new.
    /// </summary>
    public IReadOnlyList<ChatLogItem> Poll()
    {
        var result = new List<ChatLogItem>();

        ulong chatPointerMap = PointerResolver.Resolve(_mem, _pid, _chatSig);
        if (chatPointerMap <= 20) return result;

        ulong offsetArrayStart = _mem.ReadUInt64(_pid, chatPointerMap + (uint)_layout.OffsetArrayStart);
        ulong offsetArrayPos = _mem.ReadUInt64(_pid, chatPointerMap + (uint)_layout.OffsetArrayPos);
        ulong logStart = _mem.ReadUInt64(_pid, chatPointerMap + (uint)_layout.LogStart);
        ulong logNext = _mem.ReadUInt64(_pid, chatPointerMap + (uint)_layout.LogNext);

        if (offsetArrayStart == 0 || offsetArrayPos < offsetArrayStart || logStart == 0 || logNext < logStart)
            return result;

        long currentArrayIndexLong = (long)((offsetArrayPos - offsetArrayStart) / 4);
        if (currentArrayIndexLong <= 0 || currentArrayIndexLong > RingSlots)
            return result;

        int currentArrayIndex = (int)currentArrayIndexLong;

        // Pull the offset array (uint32 entries pointing into the log buffer).
        var offsetBytes = _mem.ReadBytes(_pid, offsetArrayStart, RingSlots * 4);
        if (offsetBytes.Length < RingSlots * 4)
            return result;

        var indexes = new int[RingSlots];
        for (int i = 0; i < RingSlots; i++) indexes[i] = BitConverter.ToInt32(offsetBytes, i * 4);

        if (_firstRun)
        {
            _firstRun = false;
            _previousArrayIndex = currentArrayIndex - 1;
            _previousOffset = indexes[Math.Max(0, currentArrayIndex - 1)];
            return result;
        }

        // Bulk-read just the bytes of the log buffer we'll need.
        ulong logRangeStart = logStart;
        ulong logRangeEnd = logNext;
        long logBufLenLong = (long)(logRangeEnd - logRangeStart);
        if (logBufLenLong <= 0) return result;
        if (logBufLenLong > MaxLogBufferBytes) return result;

        int logBufLen = (int)logBufLenLong;
        if (logBufLen <= 0) return result;
        var logBuf = _mem.ReadBytes(_pid, logRangeStart, logBufLen);
        if (logBuf.Length < logBufLen)
            return result;

        // Handle ring wrap: if currentArrayIndex regressed, drain to end of ring first.
        if (currentArrayIndex < _previousArrayIndex)
        {
            DrainRange(_previousArrayIndex, RingSlots, indexes, logBuf, result);
            _previousOffset = 0;
            _previousArrayIndex = 0;
        }

        if (_previousArrayIndex < currentArrayIndex)
            DrainRange(_previousArrayIndex, currentArrayIndex, indexes, logBuf, result);

        _previousArrayIndex = currentArrayIndex;
        return result;
    }

    private void DrainRange(int from, int to, int[] indexes, byte[] logBuf, List<ChatLogItem> sink)
    {
        if (from < 0 || to < 0 || from >= indexes.Length || from >= to)
            return;

        to = Math.Min(to, indexes.Length);

        for (int i = from; i < to; i++)
        {
            int currentOffset = indexes[i];
            if (currentOffset <= 0 || currentOffset > logBuf.Length)
                return;

            if (currentOffset <= _previousOffset)
            {
                _previousOffset = currentOffset;
                continue;
            }

            int len = currentOffset - _previousOffset;
            if (len <= 0 || _previousOffset < 0 || _previousOffset > logBuf.Length || _previousOffset + len > logBuf.Length)
            {
                _previousOffset = currentOffset;
                continue;
            }

            var entryBytes = new byte[len];
            Array.Copy(logBuf, _previousOffset, entryBytes, 0, len);
            var item = ChatEntry.Process(entryBytes);
            if (!string.IsNullOrEmpty(item.Combined) && item.Code.Length == 4)
                sink.Add(item);
            _previousOffset = currentOffset;
        }
    }
}
