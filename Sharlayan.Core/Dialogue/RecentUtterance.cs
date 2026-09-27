namespace Sharlayan.Core.Dialogue;

/// <summary>Deduplicates the two independent live/chat roads within one utterance.</summary>
public sealed class RecentUtterance
{
    public static readonly TimeSpan SameBreath = TimeSpan.FromSeconds(2);
    private const int Remembered = 16;
    private readonly Queue<Said> _said = new();

    public bool IsEcho(string? words, string? speaker, DateTime now)
    {
        if (Knows(words, speaker, now)) return true;
        Note(words, speaker, now);
        return false;
    }

    public void Note(string? words, string? speaker, DateTime now)
    {
        if (string.IsNullOrEmpty(words)) return;
        _said.Enqueue(new Said(words, speaker ?? string.Empty, now));
        while (_said.Count > Remembered) _said.Dequeue();
    }

    public void Forget() => _said.Clear();

    private bool Knows(string? words, string? speaker, DateTime now)
    {
        if (string.IsNullOrEmpty(words)) return false;
        speaker ??= string.Empty;
        foreach (var said in _said)
        {
            var age = now - said.At;
            if (age < TimeSpan.Zero || age >= SameBreath || !string.Equals(said.Words, words, StringComparison.Ordinal)) continue;
            if (speaker.Length == 0 || said.Speaker.Length == 0 || string.Equals(speaker, said.Speaker, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private readonly record struct Said(string Words, string Speaker, DateTime At);
}
