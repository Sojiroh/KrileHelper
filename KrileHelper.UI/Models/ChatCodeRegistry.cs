using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace KrileHelper.UI.Models;

public sealed record ChatCodeInfo(string Code, string Name, IBrush Color, int MsgType)
{
    public bool IsTranslatable => MsgType == 1;
}

// Loads the canonical chat code list bundled at Assets/ChatCodes.json. Codes
// that aren't in this list are battle/system noise and get filtered out.
public sealed class ChatCodeRegistry
{
    private readonly Dictionary<string, ChatCodeInfo> _byCode;

    public IReadOnlyDictionary<string, ChatCodeInfo> ByCode => _byCode;

    public ChatCodeRegistry(IEnumerable<ChatCodeInfo> codes)
    {
        _byCode = codes.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
    }

    public ChatCodeInfo? Lookup(string code) =>
        _byCode.TryGetValue(code, out var info) ? info : null;

    public static ChatCodeRegistry LoadDefault()
    {
        var uri = new Uri("avares://KrileHelper.UI/Assets/ChatCodes.json");
        using var stream = AssetLoader.Open(uri);
        var defs = JsonSerializer.Deserialize<List<RawChatCode>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

        var infos = defs.Select(d => new ChatCodeInfo(
            d.ChatCode,
            d.Name,
            ParseBrush(d.Color),
            d.MsgType));
        return new ChatCodeRegistry(infos);
    }

    private static IBrush ParseBrush(string hex)
    {
        if (Color.TryParse(hex, out var c)) return new SolidColorBrush(c);
        return Brushes.WhiteSmoke;
    }

    private sealed class RawChatCode
    {
        public string ChatCode { get; set; } = "";
        public string Name { get; set; } = "";
        public string Color { get; set; } = "#FFFFFFFF";
        public int MsgType { get; set; }
    }
}
