using Sharlayan.Core.Native;

namespace Sharlayan.Core.Process;

internal static class PEHeader
{
    public static PEInfo Read(INativeMemory mem, int pid, ulong moduleBase)
    {
        var dos = mem.ReadBytes(pid, moduleBase, 0x40);
        if (dos.Length < 0x40 || dos[0] != 0x4D || dos[1] != 0x5A)
            throw new InvalidOperationException($"Not a PE image at 0x{moduleBase:X} (no MZ).");
        var elfanew = (uint)BitConverter.ToInt32(dos, 0x3C);

        var hdr = mem.ReadBytes(pid, moduleBase + elfanew, 4096);
        if (hdr.Length < 4 || hdr[0] != 0x50 || hdr[1] != 0x45 || hdr[2] != 0 || hdr[3] != 0)
            throw new InvalidOperationException("Bad PE signature.");

        const int coff = 4;
        var numberOfSections = BitConverter.ToUInt16(hdr, coff + 2);
        var sizeOfOptionalHeader = BitConverter.ToUInt16(hdr, coff + 16);

        const int opt = coff + 20;
        var magic = BitConverter.ToUInt16(hdr, opt);
        bool is64 = magic == 0x20B;
        if (!is64 && magic != 0x10B)
            throw new InvalidOperationException($"Unknown Optional Header magic: 0x{magic:X}");

        // PE32+ Optional Header layout: EntryPoint @+16, ImageBase @+24 (8 bytes),
        // SizeOfImage @+56. PE32 has ImageBase @+28 (4 bytes).
        var entryPoint = BitConverter.ToUInt32(hdr, opt + 16);
        ulong imageBase = is64
            ? BitConverter.ToUInt64(hdr, opt + 24)
            : BitConverter.ToUInt32(hdr, opt + 28);
        var sizeOfImage = BitConverter.ToUInt32(hdr, opt + 56);

        var sectionTable = opt + sizeOfOptionalHeader;
        var sections = new List<PESection>(numberOfSections);
        for (int i = 0; i < numberOfSections; i++)
        {
            var s = sectionTable + i * 40;
            if (s + 40 > hdr.Length) break;
            var name = System.Text.Encoding.ASCII.GetString(hdr, s, 8).TrimEnd('\0');
            var vsize = BitConverter.ToUInt32(hdr, s + 8);
            var vaddr = BitConverter.ToUInt32(hdr, s + 12);
            var characteristics = BitConverter.ToUInt32(hdr, s + 36);
            sections.Add(new PESection(name, vaddr, vsize, characteristics));
        }

        return new PEInfo(is64, imageBase, sizeOfImage, entryPoint, sections);
    }
}
