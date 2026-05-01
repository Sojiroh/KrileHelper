namespace Sharlayan.Core.Process;

public sealed record AttachedProcess(
    int Pid,
    string ExecutablePath,
    ulong ModuleBase,
    PEInfo Pe,
    IReadOnlyList<MappedRegion> Regions)
{
    public ulong ModuleEnd => ModuleBase + Pe.SizeOfImage;
    public ulong ModuleSize => Pe.SizeOfImage;

    public bool IsAlive => System.IO.File.Exists($"/proc/{Pid}/maps");
}
