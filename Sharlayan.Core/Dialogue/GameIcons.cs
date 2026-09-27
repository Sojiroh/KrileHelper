using System.Text;

namespace Sharlayan.Core.Dialogue;

/// <summary>Private-use marks used to carry FFXIV SeString icons through translation.</summary>
public static class GameIcons
{
    private const int Origin = 0xE000;
    private const int Limit = 0xF8FF;

    public static string Mark(int iconId) => iconId > 0 && Origin + iconId <= Limit
        ? ((char)(Origin + iconId)).ToString()
        : string.Empty;

    public static bool IsMark(char character) => character >= Origin && character <= Limit;
    public static int IdOf(char character) => IsMark(character) ? character - Origin : 0;

    public static string Strip(string? line)
    {
        if (string.IsNullOrEmpty(line)) return line ?? string.Empty;
        var found = line.Any(IsMark);
        if (!found) return line;
        var builder = new StringBuilder(line.Length);
        foreach (var character in line)
            if (!IsMark(character)) builder.Append(character);
        return builder.ToString();
    }
}
