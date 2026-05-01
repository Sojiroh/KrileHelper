using Sharlayan.Core.Native;

namespace Sharlayan.Core.Scanning;

internal static class SignatureScanner
{
    public static int FindInBuffer(byte[] haystack, int hayLen, Signature sig, int start = 0)
    {
        var p = sig.Pattern;
        var end = hayLen - p.Length;
        for (int i = start; i <= end; i++)
        {
            bool ok = true;
            for (int j = 0; j < p.Length; j++)
            {
                if (p[j] >= 0 && haystack[i + j] != (byte)p[j])
                {
                    ok = false;
                    break;
                }
            }
            if (ok) return i;
        }
        return -1;
    }

    public static void ScanRangeMulti(
        INativeMemory mem,
        int pid,
        ulong start,
        ulong end,
        IReadOnlyList<Signature> sigs,
        int chunkSize = 1 << 20)
    {
        if (end <= start || sigs.Count == 0) return;

        int maxLen = 0;
        foreach (var s in sigs) if (s.Length > maxLen) maxLen = s.Length;
        int overlap = maxLen - 1;

        ulong cursor = start;
        var buf = new byte[chunkSize + overlap];

        while (cursor < end)
        {
            int wantBase = (int)Math.Min((ulong)chunkSize, end - cursor);
            int want = (int)Math.Min((ulong)(wantBase + overlap), end - cursor);
            int got;
            try { got = mem.Read(pid, cursor, buf.AsSpan(0, want)); }
            catch { cursor += (ulong)wantBase; continue; }
            if (got <= 0) { cursor += (ulong)wantBase; continue; }

            foreach (var s in sigs)
            {
                if (s.SigScanAddress != 0) continue;
                int hit = FindInBuffer(buf, got, s);
                if (hit >= 0) s.SigScanAddress = cursor + (ulong)hit + (uint)s.Length;
            }
            cursor += (ulong)wantBase;
        }
    }
}
