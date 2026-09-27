namespace Sharlayan.Core.Resources;

using Sharlayan.Core.Dialogue;


public sealed class StructuresContainer
{
    public ChatLogPointersStruct ChatLogPointers { get; set; } = new();

    /// <summary>
    /// Optional UI metadata override. Existing upstream structure resources omit
    /// it, so the pinned FFXIVClientStructs layout is used by default.
    /// </summary>
    public DialogueMemoryLayout Dialogue { get; set; } = DialogueMemoryLayout.Default;
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
