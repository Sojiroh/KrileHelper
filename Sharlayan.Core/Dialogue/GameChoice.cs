namespace Sharlayan.Core.Dialogue;

/// <summary>A visible question and its clickable answer rows.</summary>
public sealed class GameChoice
{
    public static GameChoice None { get; } = new(string.Empty, AddonBounds.Unknown,
        Array.Empty<string>(), Array.Empty<AddonBounds>());

    public GameChoice(string? question, AddonBounds questionBounds,
        IReadOnlyList<string>? answers, IReadOnlyList<AddonBounds>? answerBounds)
    {
        Question = question ?? string.Empty;
        QuestionBounds = questionBounds;
        Answers = answers ?? Array.Empty<string>();
        AnswerBounds = answerBounds ?? Array.Empty<AddonBounds>();
    }

    public string Question { get; }
    public AddonBounds QuestionBounds { get; }
    public IReadOnlyList<string> Answers { get; }
    public IReadOnlyList<AddonBounds> AnswerBounds { get; }
    public bool IsBeingAsked => Answers.Count > 0;

    public string AsBlock()
    {
        var lines = new List<string>(Answers.Count + 1);
        if (Question.Length > 0) lines.Add(Question);
        for (var i = 0; i < Answers.Count; i++) lines.Add($"{i + 1}. {Answers[i]}");
        return string.Join("\n", lines);
    }

    /// <summary>Splits a translated numbered block only when every expected row is present exactly once.</summary>
    public static bool TryReadBlock(string? block, int expectedAnswers, out string question, out string[] answers)
    {
        question = string.Empty; answers = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(block) || expectedAnswers <= 0) return false;
        var found = new string[expectedAnswers];
        var questionLines = new List<string>();
        var numbered = 0;
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var number = LeadingNumber(line, out var rest);
            var startsWithDigit = char.IsDigit(line[0]);
            if (startsWithDigit)
            {
                // Once a line looks numbered, silently treating an invalid,
                // out-of-range, or duplicate row as question text can map a
                // translation to the wrong clickable answer.
                if (number < 1 || number > expectedAnswers || found[number - 1] is not null || rest.Length == 0)
                    return false;
                found[number - 1] = rest;
                numbered++;
                continue;
            }

            // Wrapped/ambiguous answer text cannot safely be placed back on a
            // row after numbering has begun.
            if (numbered > 0) return false;
            questionLines.Add(line);
        }
        if (numbered != expectedAnswers || found.Any(answer => answer is null)) return false;
        question = string.Join(" ", questionLines);
        answers = found!;
        return true;
    }

    public string Signature() => Question + string.Join(string.Empty, Answers);
    private static int LeadingNumber(string line, out string rest)
    {
        rest = line;
        var at = 0;
        while (at < line.Length && char.IsDigit(line[at])) at++;
        if (at == 0 || at > 2 || !int.TryParse(line[..at], out var number)) return 0;
        var after = at;
        while (after < line.Length && (line[after] == '.' || line[after] == ')' ||
                                       line[after] == ':' || char.IsWhiteSpace(line[after]))) after++;
        if (after >= line.Length) return 0;
        rest = line[after..].Trim();
        return rest.Length == 0 ? 0 : number;
    }
}
