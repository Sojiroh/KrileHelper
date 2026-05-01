using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sharlayan.Core.ChatLog;

namespace KrileHelper.UI.Models;

public partial class ChatLineDisplay : ObservableObject
{
    public DateTime TimeStamp { get; }
    public string Code { get; }
    public string ChannelName { get; }
    public string Line { get; }
    public IBrush Color { get; }

    [ObservableProperty] private string? _translation;
    [ObservableProperty] private bool _translationPending;
    [ObservableProperty] private string? _translationError;

    private ChatLineDisplay(ChatLogItem item, ChatCodeInfo info)
    {
        TimeStamp = item.TimeStamp;
        Code = item.Code;
        ChannelName = info.Name;
        Line = item.Line;
        Color = info.Color;
        TranslationPending = true;
    }

    /// <summary>Returns null if the chat code is not in the registry (battle log etc.).</summary>
    public static ChatLineDisplay? TryCreate(ChatLogItem item, ChatCodeRegistry registry)
    {
        var info = registry.Lookup(item.Code);
        if (info is null) return null;
        return new ChatLineDisplay(item, info);
    }

    /// <summary>Caller already resolved the code info (e.g. when applying per-channel filters).</summary>
    public static ChatLineDisplay Create(ChatLogItem item, ChatCodeInfo info) => new(item, info);
}
