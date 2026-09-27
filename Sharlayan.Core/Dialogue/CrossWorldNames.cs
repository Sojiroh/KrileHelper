using System.Text;

namespace Sharlayan.Core.Dialogue;

/// <summary>Protects concatenated cross-world player names during translation.</summary>
public static class CrossWorldNames
{
    public const int CrossWorldIcon = 88;
    private const char FirstMark = '\uF000';
    private const int MostHidden = 32;

    public static string Hide(string? line, IReadOnlyCollection<string>? worlds, out List<string> hidden)
    {
        hidden = new List<string>();
        if (string.IsNullOrEmpty(line) || worlds is null || worlds.Count == 0) return line ?? string.Empty;
        var built = new StringBuilder(line.Length);
        var at = 0;
        while (at < line.Length)
        {
            var found = FindJoin(line, at, worlds, out var start, out var world);
            if (found < 0 || hidden.Count >= MostHidden)
            {
                built.Append(line, at, line.Length - at);
                break;
            }
            built.Append(line, at, start - at);
            built.Append((char)(FirstMark + hidden.Count));
            hidden.Add(line.Substring(start, found - start) + GameIcons.Mark(CrossWorldIcon) + world);
            at = found + world.Length;
        }
        return built.ToString();
    }

    public static string Show(string? line, IReadOnlyList<string>? hidden)
    {
        if (string.IsNullOrEmpty(line) || hidden is null || hidden.Count == 0) return line ?? string.Empty;
        var built = new StringBuilder(line.Length);
        for (var at = 0; at < line.Length; at++)
        {
            var which = line[at] - FirstMark;
            if (which < 0 || which >= hidden.Count) { built.Append(line[at]); continue; }
            if (built.Length > 0 && char.IsLetterOrDigit(built[^1])) built.Append(' ');
            built.Append(hidden[which]);
            if (at + 1 < line.Length && char.IsLetterOrDigit(line[at + 1])) built.Append(' ');
        }
        return built.ToString();
    }

    public static string Mark(string? line, IReadOnlyCollection<string>? worlds)
    {
        var without = Hide(line, worlds, out var hidden);
        return hidden.Count == 0 ? line ?? string.Empty : Show(without, hidden);
    }

    private static int FindJoin(string line, int from, IReadOnlyCollection<string> worlds,
        out int nameStart, out string world)
    {
        nameStart = -1; world = string.Empty;
        for (var i = from + 1; i < line.Length; i++)
        {
            if (!char.IsUpper(line[i]) || !char.IsLower(line[i - 1])) continue;
            foreach (var candidate in worlds)
            {
                if (!EndsAWordHere(line, i, candidate)) continue;
                nameStart = StartOfName(line, i); world = candidate; return i;
            }
        }
        return -1;
    }

    private static bool EndsAWordHere(string line, int at, string world) =>
        !string.IsNullOrEmpty(world) && at + world.Length <= line.Length &&
        string.CompareOrdinal(line, at, world, 0, world.Length) == 0 &&
        (at + world.Length >= line.Length || !char.IsLetter(line[at + world.Length]));

    private static int StartOfName(string line, int join)
    {
        var start = join;
        while (start > 0 && char.IsLetter(line[start - 1])) start--;
        if (start >= 2 && line[start - 1] == ' ' && char.IsLetter(line[start - 2]))
        {
            var forename = start - 1;
            while (forename > 0 && (char.IsLetter(line[forename - 1]) || line[forename - 1] == '\'')) forename--;
            if (forename < start - 1 && char.IsUpper(line[forename])) return forename;
        }
        return start;
    }
}
