namespace Sharlayan.Core.Process;

public sealed record PEInfo(
    bool Is64Bit,
    ulong ImageBase,
    uint SizeOfImage,
    uint EntryPointRva,
    IReadOnlyList<PESection> Sections);

public sealed record PESection(string Name, uint VirtualAddress, uint VirtualSize, uint Characteristics)
{
    public bool IsExecutable => (Characteristics & 0x20000000) != 0;
}
