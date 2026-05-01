namespace Sharlayan.Core.ChatLog;

public sealed class ChatLogItem
{
    public DateTime TimeStamp { get; set; }
    public string Code { get; set; } = "";
    public string Raw { get; set; } = "";
    public string Line { get; set; } = "";
    public string Combined => $"{Code}:{Line}";
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public bool JP { get; set; }

    public override string ToString() => $"[{TimeStamp:HH:mm:ss}] {Code}: {Line}";
}
