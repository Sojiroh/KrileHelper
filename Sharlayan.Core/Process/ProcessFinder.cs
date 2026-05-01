using System.Globalization;
using Sharlayan.Core.Native;

namespace Sharlayan.Core.Process;

internal static class ProcessFinder
{
    private const string FFXIVExecutableName = "ffxiv_dx11.exe";

    public static IReadOnlyList<MappedRegion> ParseMaps(int pid)
    {
        var path = $"/proc/{pid}/maps";
        var list = new List<MappedRegion>();
        foreach (var line in File.ReadLines(path))
        {
            var sp = line.IndexOf(' ');
            if (sp < 0) continue;

            var range = line.AsSpan(0, sp);
            var dash = range.IndexOf('-');
            if (dash < 0) continue;
            if (!ulong.TryParse(range[..dash], NumberStyles.HexNumber, null, out var start)) continue;
            if (!ulong.TryParse(range[(dash + 1)..], NumberStyles.HexNumber, null, out var end)) continue;

            var afterRange = line.AsSpan(sp + 1);
            var sp2 = afterRange.IndexOf(' ');
            var perms = sp2 < 0 ? afterRange.ToString() : afterRange[..sp2].ToString();

            // Path is the trailing field after fixed columns; take everything after the
            // last ASCII space (paths in /proc/maps don't contain spaces in practice).
            var lastSp = line.LastIndexOf(' ');
            var entryPath = lastSp >= 0 ? line[(lastSp + 1)..].Trim() : "";

            list.Add(new MappedRegion(start, end, perms, entryPath));
        }
        return list;
    }

    public static AttachedProcess? FindFFXIV(INativeMemory mem)
    {
        foreach (var procDir in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(procDir);
            if (!int.TryParse(name, out var pid)) continue;
            if (!File.Exists($"/proc/{pid}/maps")) continue;

            IReadOnlyList<MappedRegion> regions;
            try { regions = ParseMaps(pid); }
            catch { continue; }

            ulong baseAddr = 0;
            string exePath = "";
            foreach (var r in regions)
            {
                if (r.Path.EndsWith(FFXIVExecutableName, StringComparison.OrdinalIgnoreCase))
                {
                    if (baseAddr == 0 || r.Start < baseAddr)
                    {
                        baseAddr = r.Start;
                        exePath = r.Path;
                    }
                }
            }
            if (baseAddr == 0) continue;

            PEInfo pe;
            try { pe = PEHeader.Read(mem, pid, baseAddr); }
            catch { continue; }

            return new AttachedProcess(pid, exePath, baseAddr, pe, regions);
        }
        return null;
    }
}
