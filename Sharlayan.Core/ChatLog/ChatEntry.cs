using System.Text;
using System.Text.RegularExpressions;

namespace Sharlayan.Core.ChatLog;

internal static class ChatEntry
{
    private static readonly Regex InvalidXml =
        new(@"[\x00-\x08\x0B\x0C\x0E-\x1F]", RegexOptions.Compiled);

    public static ChatLogItem Process(byte[] raw)
    {
        var item = new ChatLogItem { Bytes = raw };
        if (raw.Length < 8) return item;

        uint unix = BitConverter.ToUInt32(raw, 0);
        item.TimeStamp = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;

        ushort codeRaw = BitConverter.ToUInt16(raw, 4);
        item.Code = codeRaw.ToString("X4");
        item.Raw = SafeUtf8(raw);

        var payload = new ArraySegment<byte>(raw, 8, raw.Length - 8).ToArray();
        var cleaned = ChatCleaner.ProcessFullLine(item.Code, payload);

        int cut = cleaned.Length >= 2 && cleaned[1] == ':' ? 2 : (cleaned.Length >= 1 ? 1 : 0);
        item.Line = cleaned.Length >= cut ? InvalidXml.Replace(cleaned[cut..], "") : "";
        item.JP = item.Line.Any(IsJapanese);
        return item;
    }

    private static string SafeUtf8(byte[] b)
    {
        try { return Encoding.UTF8.GetString(b); }
        catch { return ""; }
    }

    private static bool IsJapanese(char c) =>
        (c >= 0x3040 && c <= 0x309F) ||
        (c >= 0x30A0 && c <= 0x30FF) ||
        (c >= 0x4E00 && c <= 0x9FBF);
}
