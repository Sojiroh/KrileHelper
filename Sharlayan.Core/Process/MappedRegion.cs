namespace Sharlayan.Core.Process;

public sealed record MappedRegion(ulong Start, ulong End, string Perms, string Path)
{
    public ulong Size => End - Start;
    public bool IsReadable => Perms.Length > 0 && Perms[0] == 'r';
    public bool IsExecutable => Perms.Length > 2 && Perms[2] == 'x';
}
