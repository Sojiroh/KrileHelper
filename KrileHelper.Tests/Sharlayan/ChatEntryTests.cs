using System.Text;
using Sharlayan.Core.ChatLog;

namespace KrileHelper.Tests.Sharlayan;

public sealed class ChatEntryTests
{
    [Fact]
    public void Process_ExtractsTimestampCodeAndCleanedLine()
    {
        var raw = BuildRawEntry("000A", "Xこんにちは");

        var item = ChatEntry.Process(raw);

        Assert.Equal("000A", item.Code);
        Assert.Equal("こんにちは", item.Line);
        Assert.True(item.JP);
    }

    [Fact]
    public void Process_ReplacesChatDelimiterWithColonForPlayerChat()
    {
        var payload = Encoding.UTF8.GetBytes("XA").Concat(new byte[] { 0x1F }).Concat(Encoding.UTF8.GetBytes("hello")).ToArray();
        var raw = BuildRawEntry("000A", payload);

        var item = ChatEntry.Process(raw);

        Assert.Equal("A: hello", item.Line);
    }

    internal static byte[] BuildRawEntry(string code, string payload) => BuildRawEntry(code, Encoding.UTF8.GetBytes(payload));

    internal static byte[] BuildRawEntry(string code, byte[] payload)
    {
        var raw = new byte[8 + payload.Length];
        BitConverter.GetBytes(1_700_000_000U).CopyTo(raw, 0);
        BitConverter.GetBytes(Convert.ToUInt16(code, 16)).CopyTo(raw, 4);
        payload.CopyTo(raw, 8);
        return raw;
    }
}
