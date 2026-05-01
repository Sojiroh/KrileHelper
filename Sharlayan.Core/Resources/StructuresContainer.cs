namespace Sharlayan.Core.Resources;

public sealed class StructuresContainer
{
    public ChatLogPointersStruct ChatLogPointers { get; set; } = new();
}

public sealed class ChatLogPointersStruct
{
    public int LogStart { get; set; }
    public int LogNext { get; set; }
    public int LogEnd { get; set; }
    public int OffsetArrayStart { get; set; }
    public int OffsetArrayPos { get; set; }
    public int OffsetArrayEnd { get; set; }
}
