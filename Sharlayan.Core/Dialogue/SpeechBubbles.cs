namespace Sharlayan.Core.Dialogue;

/// <summary>Reports only bubble text that appeared since the previous sweep.</summary>
public sealed class SpeechBubbles
{
    private HashSet<string> _onScreen = new(StringComparer.Ordinal);

    public string Pick(IReadOnlyList<string>? bubbles)
    {
        var showing = bubbles ?? Array.Empty<string>();
        var appeared = showing.Where(bubble => !string.IsNullOrEmpty(bubble) && !_onScreen.Contains(bubble)).ToArray();
        _onScreen = showing.Where(bubble => !string.IsNullOrEmpty(bubble)).ToHashSet(StringComparer.Ordinal);
        return appeared.OrderByDescending(bubble => bubble.Length).FirstOrDefault() ?? string.Empty;
    }

    public void Forget() => _onScreen.Clear();
}
