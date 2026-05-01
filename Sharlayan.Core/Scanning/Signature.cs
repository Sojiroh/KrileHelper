namespace Sharlayan.Core.Scanning;

public sealed class Signature
{
    public string Key { get; }
    public short[] Pattern { get; }
    public IReadOnlyList<long>? PointerPath { get; }
    public bool IsAsmSignature { get; }
    public ulong SigScanAddress { get; internal set; }
    public int Length => Pattern.Length;

    public Signature(string key, string hexPattern, IReadOnlyList<long>? pointerPath = null, bool isAsm = false)
    {
        Key = key;
        Pattern = ParsePattern(hexPattern);
        PointerPath = pointerPath;
        IsAsmSignature = isAsm;
    }

    public static short[] ParsePattern(string hex)
    {
        if (hex.Contains(' '))
        {
            var tokens = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var arr = new short[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
                arr[i] = (tokens[i] == "??" || tokens[i] == "**")
                    ? (short)-1
                    : Convert.ToInt16(tokens[i], 16);
            return arr;
        }
        if (hex.Length % 2 != 0)
            throw new ArgumentException($"Pattern must have even hex length: {hex}");
        var pat = new short[hex.Length / 2];
        for (int i = 0; i < pat.Length; i++)
        {
            char a = hex[i * 2], b = hex[i * 2 + 1];
            if (a is '?' or '*' || b is '?' or '*')
                pat[i] = -1;
            else
                pat[i] = (short)(HexNibble(a) << 4 | HexNibble(b));
        }
        return pat;
    }

    private static int HexNibble(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => throw new FormatException($"Bad hex char: '{c}'"),
    };
}
