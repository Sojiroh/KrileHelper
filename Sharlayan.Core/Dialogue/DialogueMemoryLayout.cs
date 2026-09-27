namespace Sharlayan.Core.Dialogue;

/// <summary>
/// Native offsets needed to walk FFXIV's UI module. The defaults are generated
/// from the pinned Sharlayan 9.0.34 FFXIVClientStructs metadata; resource files
/// may override them for a later client. No Windows Sharlayan runtime is used.
/// </summary>
public sealed class DialogueMemoryLayout
{
    /// <summary>
    /// Layout emitted by the pinned Sharlayan 9.0.34 FFXIVClientStructs
    /// metadata. The nearby-module search in LiveDialogueReader compensates for
    /// the client-side RaptureAtkModule move observed in patch 7.56.
    /// </summary>
    public static DialogueMemoryLayout Default => new()
    {
        RaptureLogModuleOffset = 0x1AC0,
        // The legacy chat resource resolves the pointer map 0x14 bytes into RaptureLogModule.
        ChatPointerMapOffset = 0x14,
        RaptureAtkModuleOffset = 0xD2670,
        // 7.56 moved this pair by 0x60 from the pinned metadata.
        LastTalkNameOffset = 0xFEF60,
        LastTalkTextOffset = 0xFEFC8,
        AtkUnitManagerOffset = 0x2C0,
        AllLoadedUnitsListOffset = 0x6900,
        AtkUnitListEntriesOffset = 0x8,
        AtkUnitListCountOffset = 0x808,
        AtkUnitBaseNameOffset = 0x8,
        AtkUnitBaseNameLength = 32,
        AtkTextNodeNodeTextOffset = 0xD0,
        AtkUnitBaseRootNodeOffset = 0xC8,
        AtkResNodeScreenXOffset = 0x70,
        AtkResNodeScreenYOffset = 0x74,
        AtkResNodeWidthOffset = 0xA0,
        AtkResNodeHeightOffset = 0xA2,
        AtkResNodeScaleXOffset = 0x4C,
        NodeListOffset = 0x50,
        NodeListCountOffset = 0x42,
        UldManagerOffset = 0x28,
        NodeIdOffset = 0x8,
        NodeTypeOffset = 0x40,
        NodeFlagsOffset = 0xAE,
        ComponentOffset = 0xC0,
        ComponentUldManagerOffset = 0x8,
        PartIdOffset = 0xC8,
        PartsListOffset = 0xC0,
        TalkSpeakerNodeOffset = 0x238,
        TalkTextNodeOffset = 0x240,
        MiniTalkEntriesOffset = 0x248,
        MiniTalkEntrySize = 0x38,
        MiniTalkTextNodeOffset = 0x20,
        MiniTalkResNodeOffset = 0x18,
        MiniTalkEntryCount = 10,
        TalkSubtitleTextOffset = 0x238,
        PlayerStateNameOffset = 1,
        PlayerStateSexOffset = 123,
        PlayerStateSourceSize = 2336
    };


    public static DialogueMemoryLayout Unavailable => new();

    public long RaptureLogModuleOffset { get; init; } = -1;
    public long ChatPointerMapOffset { get; init; }
    public long RaptureAtkModuleOffset { get; init; } = -1;
    public long LastTalkNameOffset { get; init; } = -1;
    public long LastTalkTextOffset { get; init; } = -1;
    public long AtkUnitManagerOffset { get; init; } = -1;
    public long AllLoadedUnitsListOffset { get; init; } = -1;
    public long AtkUnitListEntriesOffset { get; init; } = -1;
    public long AtkUnitListCountOffset { get; init; } = -1;
    public long AtkUnitBaseNameOffset { get; init; } = -1;
    public int AtkUnitBaseNameLength { get; init; }
    public long AtkTextNodeNodeTextOffset { get; init; } = -1;
    public long AtkUnitBaseRootNodeOffset { get; init; } = -1;
    public long AtkResNodeScreenXOffset { get; init; } = -1;
    public long AtkResNodeScreenYOffset { get; init; } = -1;
    public long AtkResNodeWidthOffset { get; init; } = -1;
    public long AtkResNodeHeightOffset { get; init; } = -1;
    public long AtkResNodeScaleXOffset { get; init; } = -1;
    public long NodeListOffset { get; init; } = -1;
    public long NodeListCountOffset { get; init; } = -1;
    public long UldManagerOffset { get; init; } = -1;
    public long NodeIdOffset { get; init; } = -1;
    public long NodeTypeOffset { get; init; } = -1;
    public long NodeFlagsOffset { get; init; } = -1;
    public long ComponentOffset { get; init; } = -1;
    public long ComponentUldManagerOffset { get; init; } = -1;
    public long PartIdOffset { get; init; } = -1;
    public long PartsListOffset { get; init; } = -1;

    public long TalkSpeakerNodeOffset { get; init; } = -1;
    public long TalkTextNodeOffset { get; init; } = -1;
    public long MiniTalkEntriesOffset { get; init; } = -1;
    public int MiniTalkEntrySize { get; init; }
    public long MiniTalkTextNodeOffset { get; init; } = -1;
    public long MiniTalkResNodeOffset { get; init; } = -1;
    public int MiniTalkEntryCount { get; init; }
    public long TalkSubtitleTextOffset { get; init; } = -1;
    public long PlayerStateNameOffset { get; init; } = -1;
    public long PlayerStateSexOffset { get; init; } = -1;
    public int PlayerStateSourceSize { get; init; }

    public bool HasWindowLayout => RaptureLogModuleOffset >= 0 && RaptureAtkModuleOffset >= 0 &&
        AtkUnitManagerOffset >= 0 && AllLoadedUnitsListOffset >= 0 && AtkUnitListEntriesOffset >= 0 &&
        AtkUnitListCountOffset >= 0 && AtkUnitBaseNameOffset >= 0 && AtkUnitBaseNameLength > 0;

    public bool HasNodeLayout => UldManagerOffset >= 0 && NodeListOffset >= 0 && NodeListCountOffset >= 0 &&
        NodeIdOffset >= 0 && NodeTypeOffset >= 0 && NodeFlagsOffset >= 0 && AtkTextNodeNodeTextOffset >= 0;

    public bool HasBounds => AtkUnitBaseRootNodeOffset >= 0 && AtkResNodeScreenXOffset >= 0 &&
        AtkResNodeScreenYOffset >= 0 && AtkResNodeWidthOffset >= 0 && AtkResNodeHeightOffset >= 0 &&
        AtkResNodeScaleXOffset >= 0;

    public bool IsUsable => HasWindowLayout && HasNodeLayout &&
        (TalkSpeakerNodeOffset >= 0 || TalkTextNodeOffset >= 0 || TalkSubtitleTextOffset >= 0 || MiniTalkEntriesOffset >= 0);
}
