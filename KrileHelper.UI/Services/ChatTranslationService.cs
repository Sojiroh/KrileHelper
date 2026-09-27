using System.Text;
using Sharlayan.Core.Dialogue;
using Translation.Core;

namespace KrileHelper.UI.Services;

public sealed record ChatTranslationContext(string SourceLanguage, string TargetLanguage,
    bool TranslateNpcNames, bool TranslatePlayerNames, bool UseReference, string ReferenceSourceLanguage,
    string PlayerName, bool? PlayerIsFeminine, IReadOnlyCollection<string> Worlds);

public sealed record ChatTranslation(string Text, string Speaker, string Body, string Engine);

/// <summary>Reference lookup precedes name masking; names never enter a machine request unless opted in.</summary>
public sealed class ChatTranslationService(ReferenceTranslationService reference)
{
    public static bool IsNpcChannel(string code) => code is "003D" or "0044" or "2AB9";

    public static bool IsPlayerChannel(string code) =>
        int.TryParse(code, System.Globalization.NumberStyles.HexNumber, null, out var value) &&
        (value is >= 0x0A and <= 0x1E or 0x25 or >= 0x65 and <= 0x6B);

    public static (string Speaker, string Body) SplitSpeaker(string line, string code)
    {
        if (!IsNpcChannel(code) && !IsPlayerChannel(code)) return ("", line);
        int at = line.AsSpan().IndexOfAny(':', '：');
        if (at <= 0 || at > 80 || line.AsSpan(0, at).Contains('\n')) return ("", line);
        return (line[..at].Trim(), line[(at + 1)..].TrimStart());
    }

    public async Task<ChatTranslation> TranslateAsync(ITranslator translator, string line, string code,
        ChatTranslationContext context, CancellationToken cancellationToken = default, string? explicitSpeaker = null)
    {
        var (speaker, body) = explicitSpeaker is null ? SplitSpeaker(line, code) : (explicitSpeaker, line);
        var translatedSpeaker = CrossWorldNames.Mark(speaker, context.Worlds);
        var translateName = IsNpcChannel(code) ? context.TranslateNpcNames :
            IsPlayerChannel(code) && context.TranslatePlayerNames;
        if (translateName && speaker.Length > 0 && speaker != context.PlayerName)
        {
            if (!(context.UseReference && IsNpcChannel(code) && reference.TryTranslateSpeaker(speaker,
                    context.ReferenceSourceLanguage, context.TargetLanguage, out translatedSpeaker)))
                translatedSpeaker = (await TranslateBodyAsync(translator, speaker, context, cancellationToken)).Text;
        }
        var translated = await TranslateBodyAsync(translator, body, context, cancellationToken);
        return new ChatTranslation(translatedSpeaker.Length == 0 ? translated.Text : $"{translatedSpeaker}: {translated.Text}",
            translatedSpeaker, translated.Text, translated.Engine);
    }

    private async Task<(string Text, string Engine)> TranslateBodyAsync(ITranslator translator, string body,
        ChatTranslationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.UseReference && reference.TryTranslate(body, context.ReferenceSourceLanguage, context.TargetLanguage,
                context.PlayerName, context.PlayerIsFeminine, out var referenceText))
            return (referenceText, "XIV Rus Translation");
        if (context.UseReference && body.Contains('\n'))
        {
            var lines = body.Split('\n');
            var matches = new string?[lines.Length];
            var prefixes = new string[lines.Length];
            bool anyMatch = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var content = lines[i].Trim();
                int numberEnd = 0;
                while (numberEnd < content.Length && char.IsDigit(content[numberEnd])) numberEnd++;
                int prefixEnd = numberEnd;
                if (prefixEnd > 0 && prefixEnd < content.Length && content[prefixEnd] is '.' or ')' or ':')
                    prefixEnd++;
                if (prefixEnd > 0 && prefixEnd < content.Length && char.IsWhiteSpace(content[prefixEnd]))
                {
                    while (prefixEnd < content.Length && char.IsWhiteSpace(content[prefixEnd])) prefixEnd++;
                    prefixes[i] = content[..prefixEnd];
                    content = content[prefixEnd..];
                }
                else prefixes[i] = "";
                lines[i] = content;
                if (reference.TryTranslate(content, context.ReferenceSourceLanguage, context.TargetLanguage,
                        context.PlayerName, context.PlayerIsFeminine, out var matched))
                {
                    matches[i] = matched;
                    anyMatch = true;
                }
            }
            if (anyMatch)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    var content = matches[i] ?? (lines[i].Length == 0 ? "" :
                        (await TranslateMachineAsync(translator, lines[i], context, cancellationToken)).Text);
                    lines[i] = prefixes[i] + content;
                }
                return (string.Join('\n', lines), $"XIV Rus Translation + {translator.Name}");
            }
        }
        return await TranslateMachineAsync(translator, body, context, cancellationToken);
    }

    private static async Task<(string Text, string Engine)> TranslateMachineAsync(ITranslator translator, string body,
        ChatTranslationContext context, CancellationToken cancellationToken)
    {
        var hidden = CrossWorldNames.Hide(body, context.Worlds, out var worldNames);
        var protectedName = PlayerNameProtection.Hide(hidden, context.PlayerName);
        var result = await translator.TranslateAsync(protectedName.Text, context.SourceLanguage,
            context.TargetLanguage, cancellationToken).ConfigureAwait(false);
        return (CrossWorldNames.Show(protectedName.Restore(result.TranslatedText), worldNames), result.EngineName);
    }
}

public sealed class PlayerNameProtection
{
    private readonly List<(char Mark, string Name)> _names = new(2);
    public string Text { get; private set; }
    private PlayerNameProtection(string text) => Text = text;

    public static PlayerNameProtection Hide(string text, string playerName)
    {
        var result = new PlayerNameProtection(text);
        if (string.IsNullOrWhiteSpace(playerName)) return result;
        result.HideWord(playerName);
        var space = playerName.IndexOf(' ');
        if (space > 0) result.HideWord(playerName[..space]);
        return result;
    }

    private void HideWord(string word)
    {
        if (!Text.Contains(word, StringComparison.Ordinal)) return;
        char mark = '\uE800';
        while (Text.Contains(mark) && mark < '\uE8FF') mark++;
        if (Text.Contains(mark)) throw new TranslationException("Cannot protect the character name in this message.");
        StringBuilder? built = null;
        int at = 0;
        while (at < Text.Length)
        {
            int found = Text.IndexOf(word, at, StringComparison.Ordinal);
            if (found < 0) break;
            int end = found + word.Length;
            if ((found == 0 || !char.IsLetterOrDigit(Text[found - 1])) &&
                (end == Text.Length || !char.IsLetterOrDigit(Text[end])))
            {
                built ??= new StringBuilder(Text.Length);
                built.Append(Text, at, found - at).Append(mark);
                at = end;
            }
            else
            {
                built ??= new StringBuilder(Text.Length);
                built.Append(Text, at, end - at);
                at = end;
            }
        }
        if (built is null) return;
        built.Append(Text, at, Text.Length - at);
        var hidden = built.ToString();
        if (hidden.Contains(mark)) _names.Add((mark, word));
        Text = hidden;
    }

    public string Restore(string translated)
    {
        foreach (var (mark, name) in _names)
        {
            if (!translated.Contains(mark))
                throw new TranslationException("The translation service dropped a protected character name.");
            translated = translated.Replace(mark.ToString(), name, StringComparison.Ordinal);
        }
        return translated;
    }
}
