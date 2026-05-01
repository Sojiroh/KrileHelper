using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Sharlayan.Core.ChatLog;

internal static class ChatCleaner
{
    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.ExplicitCapture;
    private static readonly Regex PlayerChatCodes = new(@"^00(0[A-F]|1[0-9A-F])$", Opts);
    private static readonly Regex NewLine = new(@"[\r\n]+", RegexOptions.Compiled);
    private static readonly Regex NoPrint = new(@"[\x00-\x1F]+", RegexOptions.Compiled);
    private static readonly Regex SpecialPurposeUnicode = new(@"[\uE000-\uF8FF]", RegexOptions.Compiled);
    private static readonly Regex SpecialReplacement = new(@"[\uFFFD]", RegexOptions.Compiled);

    public static string ProcessFullLine(string code, byte[] bytes)
    {
        var newList = new List<byte>(bytes.Length);
        var marker = new List<byte>(32);

        for (int x = 0; x < bytes.Length; x++)
        {
            byte b = bytes[x];
            switch (b)
            {
                case 0x02:
                    if (x + 2 >= bytes.Length) break;
                    int length = bytes[x + 2];
                    int limit = length - 1;
                    if (length > 1 && x + 3 + limit <= bytes.Length)
                    {
                        x += 3;
                        marker.Add((byte)'[');
                        for (int k = 0; k < limit; k++)
                            marker.AddRange(Encoding.UTF8.GetBytes(bytes[x + k].ToString("X2")));
                        marker.Add((byte)']');

                        var token = Encoding.UTF8.GetString(marker.ToArray());
                        if (token == "[59]") newList.Add(0x40);   // '@' substitution
                        marker.Clear();
                        x += limit;
                    }
                    else if (x + 4 < bytes.Length)
                    {
                        x += 4;
                        newList.Add(0x20);
                        newList.Add(bytes[x]);
                    }
                    break;

                case 0x1F:
                    newList.Add((byte)':');
                    if (PlayerChatCodes.IsMatch(code)) newList.Add((byte)' ');
                    break;

                default:
                    newList.Add(b);
                    break;
            }
        }

        var line = WebUtility.HtmlDecode(Encoding.UTF8.GetString(newList.ToArray())).Replace("  ", " ");
        line = SpecialPurposeUnicode.Replace(line, "");
        line = SpecialReplacement.Replace(line, "");
        line = NewLine.Replace(line, "");
        line = NoPrint.Replace(line, "");
        return line;
    }
}
