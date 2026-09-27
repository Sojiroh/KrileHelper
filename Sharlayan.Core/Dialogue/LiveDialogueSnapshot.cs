using Sharlayan.Core.ChatLog;

namespace Sharlayan.Core.Dialogue;

/// <summary>One coherent read of the visible dialogue and choice surfaces.</summary>
public sealed class LiveDialogueSnapshot
{
    public static LiveDialogueSnapshot Unavailable(string status = "unavailable") => new(
        Array.Empty<ChatLogItem>(), string.Empty, string.Empty, string.Empty,
        DialogueSurface.None, AddonBounds.Unknown, null, AddonBounds.Unknown,
        false, false, status);

    public LiveDialogueSnapshot(
        IReadOnlyList<ChatLogItem>? lines,
        string? speaker,
        string? text,
        string? code,
        DialogueSurface surface,
        AddonBounds bounds,
        GameChoice? choice,
        AddonBounds choiceBounds,
        bool isVisible,
        bool sourceAvailable,
        string? status)
    {
        Lines = lines ?? Array.Empty<ChatLogItem>();
        Speaker = speaker ?? string.Empty;
        Text = text ?? string.Empty;
        Code = code ?? string.Empty;
        Surface = surface;
        Bounds = bounds;
        Choice = choice;
        ChoiceBounds = choiceBounds;
        IsVisible = isVisible;
        SourceAvailable = sourceAvailable;
        Status = status ?? string.Empty;
    }

    public IReadOnlyList<ChatLogItem> Lines { get; }
    public string Speaker { get; }
    public string Text { get; }
    public string Code { get; }
    public DialogueSurface Surface { get; }
    public AddonBounds Bounds { get; }
    public GameChoice? Choice { get; }
    public AddonBounds ChoiceBounds { get; }
    public bool IsVisible { get; }
    public bool SourceAvailable { get; }
    public string Status { get; }
}
