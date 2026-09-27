using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Translation.Core.Reference
{
    /// <summary>
    /// Turns the xivrus export into something a line read off the screen can be
    /// found in.
    ///
    /// The export is XLIFF, one file per language per sheet, and the shape it
    /// arrives in is not the shape dialogue reaches us in. Every rule here was
    /// arrived at by comparing a line that came out machine-translated against
    /// what the export held for it:
    ///
    /// - A translated file's &lt;source&gt; holds the row id, not the text, so
    ///   the English comes from the file beside it, joined on that id.
    /// - Quest text is stored as KEY&lt;tab&gt;TEXT, and only the first tab
    ///   separates; the rest are tabs in the line.
    /// - Some markup only styles what is already there and the words survive on
    ///   screen; some is drawn as the literal text it looks like; and some the
    ///   game substitutes as it draws. Only the last makes a line unusable.
    /// - Battle and cutscene lines carry their speaker inside the text.
    /// </summary>
    public sealed class ReferenceIndexBuilder
    {
        /// <summary>One row of a sheet: its number, and the text against it.</summary>
        internal readonly struct Unit
        {
            public Unit(string id, string text)
            {
                Id = id;
                Text = text;
            }

            public string Id { get; }

            public string Text { get; }
        }

        private static readonly XmlReaderSettings ReaderSettings = new XmlReaderSettings
        {
            // Nothing in the export declares one, and a document that fetches
            // an external one is not something to honour while reading a
            // hundred thousand files off the internet.
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = true
        };

        /// <summary>
        /// Styling: emphasis, the pair around a highlighted term, page and word
        /// breaks, colour. The words around it are drawn as written, so taking
        /// the tags out leaves a line that matches - discarding the line instead
        /// costs 140,000 of them.
        /// </summary>
        private static readonly Regex Formatting = new Regex(
            "<var (?:1A|48|49|17|1B|1C|60)[^>]*>|</?(?:color2|glow2|color|glow)[^>]*>",
            RegexOptions.Compiled);

        /// <summary>
        /// A hyphen the game will not break a line at, and it draws one: 4,267
        /// of them in the export, and every sample is a place English puts a
        /// hyphen - Kan-E-Senna, Radz-at-Han, Toto-Rak, city-state, ill-fated,
        /// Hatching-tide.
        ///
        /// It sat among the formatting and was deleted, so the index held
        /// "Elder Seedseer KanESenna" against a screen that says
        /// "Kan-E-Senna", and every line naming one of those places or people
        /// went to an engine. An ordinary hyphen, because that is the character
        /// the game hands the reader - checked against the dialogue it has
        /// actually given us, which carries U+002D and no other dash.
        /// </summary>
        private static readonly Regex NonBreakingHyphen = new Regex("<var 1F[^>]*>", RegexOptions.Compiled);

        private static readonly Regex LineBreak = new Regex("<nl>", RegexOptions.Compiled);

        /// <summary>
        /// A place the game may break a long word, drawn as nothing until it
        /// does. German is full of them - "Waffenfertigkeiten" is stored as
        /// "Waf&lt;var 16 /var&gt;fen&lt;var 16 /var&gt;fer&lt;var 16 /var&gt;tig..." - and
        /// counting them as substitutions threw the line away.
        /// </summary>
        private static readonly Regex SoftHyphen = new Regex("<var 16[^>]*>", RegexOptions.Compiled);

        /// <summary>
        /// A space the game will not break a line at: between a number and its
        /// unit, or before the ellipsis that German and French set off with
        /// one. Drawn as a space, so it is one.
        /// </summary>
        private static readonly Regex HardSpace = new Regex("<var 1D[^>]*>", RegexOptions.Compiled);

        /// <summary>
        /// Anything the game substitutes as it draws. Recognised by being a
        /// &lt;var&gt;: sound cues like &lt;sigh&gt; are drawn as the text they
        /// look like and belong in the line.
        /// </summary>
        private static readonly Regex Dynamic = new Regex("<var [^>]*>", RegexOptions.Compiled);

        /// <summary>
        /// The player's own name, written in as the game draws.
        ///
        /// It comes two ways. Bare, as &lt;var 29 EB02 /var&gt;, and wrapped in a
        /// &lt;var 2C&gt; that picks between spellings -
        /// &lt;var 2C ((&lt;var 29 EB02 /var&gt;)) (( )) 02 /var&gt;. Only the wrapped
        /// one was recognised, and the bare one is the commoner of the two:
        /// 14,515 lines against 14,086 in the export of 5 August. Every line
        /// carrying it was thrown away as unsubstitutable markup, including
        /// whole quests' worth of the game addressing the player by name.
        ///
        /// The wrapper is listed first so it is taken whole. Matching the tag
        /// inside it instead would leave the wrapper's own debris behind, and
        /// that debris would throw the line away just the same.
        ///
        /// EB02 alone, not the whole &lt;var 29&gt; family: EA01 through EA09 and
        /// the rest are other things the game fills in, and writing the
        /// player's name over a number would be worse than dropping the line.
        /// </summary>
        private static readonly Regex Player = new Regex(
            "<var 2C .*?\\)\\) [0-9A-F]{2} /var>|<var 29 EB02 /var>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// An item's name, written in as the game draws.
        ///
        /// Worth punching a hole for rather than dropping the line: the project
        /// leaves item names in English, so whatever the game drew there can be
        /// carried straight across into the translation without having to know
        /// which item it was.
        /// </summary>
        private static readonly Regex Item = new Regex(
            "<var 31[^>]*>", RegexOptions.Compiled);

        /// <summary>
        /// Agreement with the player's gender, feminine first. E905 alone: the
        /// other condition codes ask about things that cannot be answered here,
        /// and one of them distinguishes a joystick from a mini-joystick.
        /// </summary>
        private static readonly Regex GenderAgreement = new Regex(
            "<var 08 E905 \\(\\((.*?)\\)\\) \\(\\((.*?)\\)\\) /var>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// A choice the game makes as it draws, with the wording for every
        /// outcome written out beside it: the time of day, the player's race,
        /// which Grand Company they signed with.
        ///
        /// Unlike gender, the outcome need not be known. What reaches us has
        /// been drawn already, so every wording is worth keeping and whichever
        /// one the game picked is the one that will be looked up.
        ///
        /// E905 is left out: gender is answered rather than guessed, and the
        /// store above keeps it by the answer.
        /// </summary>
        private static readonly Regex ChoiceStart = new Regex(
            "<var (?:08|09) (?!E905 )([0-9A-F]+) ", RegexOptions.Compiled);

        /// <summary>
        /// How many wordings of one line are worth keeping. A line asking two
        /// questions of eight answers each has 64, and nobody says 64 things.
        /// Past this the rest are left out rather than the line dropped: each
        /// wording kept is a true pair whether or not its siblings are there.
        /// </summary>
        private const int MaxRenderings = 32;

        private static readonly Regex SpeakerPrefix = new Regex(
            "^\\(-([^)]{0,60})-\\)", RegexOptions.Compiled);

        /// <summary>
        /// A row number from the translation tool, left at the front of the
        /// text: "9547_Cancel registration."
        ///
        /// It is not part of any line. 1 589 rows in the export carry one, and
        /// 1 485 of those are the English source with nothing else done to
        /// them - so once the number is gone they read as identical to the
        /// English and are dropped, which is what should have happened all
        /// along. The other 72 are real translations someone typed after the
        /// number, "243_Огонь" and the like, and taking the number off is what
        /// lets them be found at all.
        ///
        /// The English side never carries one, checked across the whole
        /// export, so nothing is at risk from taking it off both.
        /// </summary>
        private static readonly Regex SourceKeyPrefix = new Regex("^\\d+_[ \\t]?", RegexOptions.Compiled);

        /// <summary>
        /// Which set of parsing rules built an index.
        ///
        /// The revision says whether the translation has moved; it says nothing
        /// about whether the rules that read it have. They have moved four
        /// times in one day - a name on either side of a pair, hyphenation
        /// points, hard spaces, and a row that borrowed the next row's text -
        /// and an index at the current revision would have been left with none
        /// of it, "already up to date" and quietly wrong.
        ///
        /// Raise this whenever a change here would put something different in
        /// the index for the same export.
        /// </summary>
        public const int RulesVersion = 7;

        private const string KeySeparator = "<tab>";

        /// <summary>Stands in for the name; a control character cannot collide.</summary>
        public const string PlayerPlaceholder = "\u0001";

        /// <summary>
        /// Stands in for an item the game names as it draws.
        ///
        /// Unlike the player's name there is no one value to write in: the
        /// shopkeeper says the same sentence whichever tomestone she is
        /// selling. So this one stays a hole, and whatever the game drew there
        /// is carried across from the line being looked up.
        /// </summary>
        public const string ItemPlaceholder = "\u0002";

        /// <summary>The game's own list of who everyone is, as "Name&lt;tab&gt;Title".</summary>
        private const string NpcSheet = "ENpcResident";

        private readonly Dictionary<string, string> _lines = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _patterns = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _speakers = new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> _itemPatterns =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Dictionary<(string Source, bool Feminine), string> _gendered =
            new Dictionary<(string, bool), string>();

        private readonly Dictionary<(string Source, bool Feminine), string> _genderedPatterns =
            new Dictionary<(string, bool), string>();

        public IReadOnlyDictionary<string, string> Lines => _lines;

        public IReadOnlyDictionary<string, string> Patterns => _patterns;

        /// <summary>
        /// Lines with one item's name punched out. Matched by what stands on
        /// either side of the hole rather than filled in ahead of time - which
        /// item it is cannot be known until the line arrives.
        /// </summary>
        public IReadOnlyDictionary<string, string> ItemPatterns => _itemPatterns;

        public IReadOnlyDictionary<string, string> Speakers => _speakers;

        public IReadOnlyDictionary<(string Source, bool Feminine), string> Gendered => _gendered;

        /// <summary>
        /// Lines that both name the character and agree with them: the name is
        /// punched out, and there is one of these per gender.
        /// </summary>
        public IReadOnlyDictionary<(string Source, bool Feminine), string> GenderedPatterns => _genderedPatterns;

        public int SkippedForMarkup { get; private set; }

        /// <summary>Sheets that had an English side to pair with, so were read.</summary>
        public int Sheets { get; private set; }

        public int Conflicts { get; private set; }

        /// <summary>
        /// Files that stopped being XML partway. Whatever was read before the
        /// break is kept; this says how often that happened, because an export
        /// where it happens often is an export to look at rather than to read.
        /// </summary>
        public int MalformedSheets { get; private set; }

        /// <summary>
        /// Takes one sheet: the English file and its translation, as they stand
        /// in the export.
        /// </summary>
        public void AddSheet(string folder, string englishXliff, string translatedXliff)
        {
            var englishUnits = ReadUnits(englishXliff, out var englishMalformed);
            if (englishMalformed)
            {
                MalformedSheets++;
            }

            if (englishUnits.Count == 0)
            {
                return;
            }

            // Looked up by row number from here on. A number written twice
            // keeps the last, which is what reading them in order gave before.
            var english = new Dictionary<string, string>(englishUnits.Count, StringComparer.Ordinal);
            foreach (var unit in englishUnits)
            {
                english[unit.Id] = unit.Text;
            }

            Sheets++;

            var isRoster = FolderName(folder) == NpcSheet;

            var translatedUnits = ReadUnits(translatedXliff, out var translatedMalformed);
            if (translatedMalformed)
            {
                MalformedSheets++;
            }

            foreach (var translatedUnit in translatedUnits)
            {
                if (!english.TryGetValue(translatedUnit.Id, out var englishText))
                {
                    continue;
                }

                var translatedText = translatedUnit.Text;

                if (isRoster)
                {
                    // "Name<tab>Title" - only the name is ever spoken.
                    AddSpeaker(FirstField(englishText), FirstField(translatedText));
                    continue;
                }

                var englishSpeaker = SpeakerPrefix.Match(StripKey(englishText));
                var translatedSpeaker = SpeakerPrefix.Match(StripKey(translatedText));
                if (englishSpeaker.Success && translatedSpeaker.Success)
                {
                    AddSpeaker(englishSpeaker.Groups[1].Value, translatedSpeaker.Groups[1].Value);
                }

                Add(englishText, translatedText);
            }
        }

        private void Add(string englishText, string translatedText)
        {
            var remaining = MaxRenderings;
            AddChoices(Normalize(englishText), Normalize(translatedText), ref remaining);
        }

        /// <summary>
        /// Writes down every wording the line can be drawn as.
        ///
        /// The two sides have to be making the same choice, in the same order,
        /// with the same number of outcomes - then the first outcome of one is
        /// the first outcome of the other and they can be paired off. When they
        /// disagree there is no telling which wording answers which, and the
        /// line is left as it stands to be dropped for the markup it carries,
        /// which is what happened to it before any of this.
        /// </summary>
        private void AddChoices(string english, string translated, ref int remaining)
        {
            if (remaining <= 0)
            {
                return;
            }

            var hasEnglish = TryReadChoice(english, out var englishChoice);
            var hasTranslated = TryReadChoice(translated, out var translatedChoice);

            if (!hasEnglish && !hasTranslated)
            {
                remaining--;
                AddRendering(english, translated);
                return;
            }

            if (hasEnglish != hasTranslated ||
                !string.Equals(englishChoice.Code, translatedChoice.Code, StringComparison.Ordinal) ||
                englishChoice.Arms.Count != translatedChoice.Arms.Count)
            {
                remaining--;
                AddRendering(english, translated);
                return;
            }

            for (var arm = 0; arm < englishChoice.Arms.Count; arm++)
            {
                AddChoices(
                    englishChoice.Draw(english, arm),
                    translatedChoice.Draw(translated, arm),
                    ref remaining);
            }
        }

        private void AddRendering(string english, string translated)
        {
            if (english.Length == 0 || translated.Length == 0 ||
                string.Equals(english, translated, StringComparison.Ordinal))
            {
                return;
            }

            // Gender agreement with nothing else left to substitute: keep the
            // line both ways. Both sides can carry it - English says "this
            // woman" against "this man" - so the key differs by character too.
            if (GenderAgreement.IsMatch(english) || GenderAgreement.IsMatch(translated))
            {
                var feminineEnglish = GenderAgreement.Replace(english, m => m.Groups[1].Value);
                var masculineEnglish = GenderAgreement.Replace(english, m => m.Groups[2].Value);
                var feminine = GenderAgreement.Replace(translated, m => m.Groups[1].Value);
                var masculine = GenderAgreement.Replace(translated, m => m.Groups[2].Value);

                if (!Dynamic.IsMatch(feminineEnglish) && !Dynamic.IsMatch(masculineEnglish) &&
                    !Dynamic.IsMatch(feminine) && !Dynamic.IsMatch(masculine))
                {
                    _gendered[(feminineEnglish, true)] = feminine;
                    _gendered[(masculineEnglish, false)] = masculine;
                    return;
                }

                // What is left after the gender is settled may be the player's
                // name, and that we can also write in. A line carrying both was
                // reaching neither store: this branch gave up on account of the
                // name, and the one below gave up on account of the gender, so
                // the game's most personal lines - addressed to you, agreeing
                // with you - were the ones nobody indexed.
                if (AddGenderedPattern(feminineEnglish, feminine, true) &&
                    AddGenderedPattern(masculineEnglish, masculine, false))
                {
                    return;
                }
            }

            // At most one placeholder a side, and at least one somewhere:
            // usable as a pattern. More than one and the pieces between them
            // stop pinning the line down.
            //
            // The two sides need not agree about naming the character, and
            // requiring that they did cost whole conversations. Languages
            // address the player in different places: German says "Und er wird
            // deine Hilfe benötigen", where the Russian for the same row says
            // "Поторопись, <name>". Whichever side carries the name, the other
            // is a fixed string - so the line can still be matched, and the
            // name written into whichever side has room for it.
            var englishPlayer = Player.Replace(english, PlayerPlaceholder);
            var translatedPlayer = Player.Replace(translated, PlayerPlaceholder);
            var sourceNames = Count(englishPlayer, PlayerPlaceholder);
            var translatedNames = Count(translatedPlayer, PlayerPlaceholder);
            var isPattern = sourceNames <= 1 && translatedNames <= 1 && sourceNames + translatedNames > 0;

            var target = _lines;
            if (isPattern)
            {
                english = englishPlayer;
                translated = translatedPlayer;
                target = _patterns;
            }
            else
            {
                // An item's name is the other hole worth punching, and it is
                // kept apart from the player's: that one is written in once,
                // when the character becomes known, while this one has to be
                // read off the line being looked up.
                //
                // Only when the player is not named too. Two holes and what is
                // left stops pinning the line down, which is the same reason
                // the name allows itself only one.
                var englishItem = Item.Replace(english, ItemPlaceholder);
                var translatedItem = Item.Replace(translated, ItemPlaceholder);

                if (Count(englishItem, ItemPlaceholder) == 1 &&
                    Count(translatedItem, ItemPlaceholder) == 1)
                {
                    english = englishItem;
                    translated = translatedItem;
                    target = _itemPatterns;
                }
            }

            // Whatever is left after the holes were punched. A line still
            // carrying something the game substitutes cannot be matched, so it
            // goes - which is also what catches a line that has an item's name
            // and something else besides.
            if (Dynamic.IsMatch(english) || Dynamic.IsMatch(translated))
            {
                SkippedForMarkup++;
                return;
            }

            if (!target.TryGetValue(english, out var existing))
            {
                target[english] = translated;
            }
            else if (!string.Equals(existing, translated, StringComparison.Ordinal))
            {
                // The same line said in two places, rendered differently.
                // Keeping the first is arbitrary but stable.
                Conflicts++;
            }
        }

        /// <summary>
        /// Stores one gender's wording of a line that also names the player,
        /// with the name punched out. False when what is left still carries
        /// something the game would substitute, and then the pair is no use.
        /// </summary>
        private bool AddGenderedPattern(string english, string translated, bool feminine)
        {
            var englishPattern = Player.Replace(english, PlayerPlaceholder);
            var translatedPattern = Player.Replace(translated, PlayerPlaceholder);

            if (Count(englishPattern, PlayerPlaceholder) > 1 ||
                Count(translatedPattern, PlayerPlaceholder) > 1 ||
                Dynamic.IsMatch(englishPattern) || Dynamic.IsMatch(translatedPattern))
            {
                return false;
            }

            _genderedPatterns[(englishPattern, feminine)] = translatedPattern;
            return true;
        }

        private void AddSpeaker(string englishName, string translatedName)
        {
            var english = FoldApostrophes(Collapse(englishName));
            var translated = Collapse(SourceKeyPrefix.Replace(translatedName, string.Empty));

            if (english.Length == 0 || translated.Length == 0 ||
                string.Equals(english, translated, StringComparison.Ordinal))
            {
                return;
            }

            if (Dynamic.IsMatch(english) || Dynamic.IsMatch(translated))
            {
                return;
            }

            // "???" is the game keeping an identity back, and the Russian
            // wrapper gives it away. A label with no letters in it is a
            // placeholder, and translating it spoils the scene it was hiding.
            if (!english.Any(char.IsLetter))
            {
                return;
            }

            _speakers[english] = translated;
        }

        /// <summary>
        /// Reduces a stored line to the form the reader hands us: the game wraps
        /// dialogue where it likes and we read it joined.
        /// </summary>
        internal static string Normalize(string text)
        {
            text = SourceKeyPrefix.Replace(StripKey(text), string.Empty);
            text = Formatting.Replace(text, string.Empty);
            text = SoftHyphen.Replace(text, string.Empty);
            text = NonBreakingHyphen.Replace(text, "-");
            text = HardSpace.Replace(text, " ");
            text = LineBreak.Replace(text, " ");
            text = SpeakerPrefix.Replace(text.TrimStart(), string.Empty);
            return Collapse(text.Replace(KeySeparator, " "));
        }

        internal static string FoldApostrophes(string text)
        {
            return text.Replace('’', '\'').Replace('ʼ', '\'').Replace('‘', '\'');
        }

        private static string StripKey(string text)
        {
            var separator = text.IndexOf(KeySeparator, StringComparison.Ordinal);
            return separator >= 0 ? text.Substring(separator + KeySeparator.Length) : text;
        }

        private static string FirstField(string text)
        {
            var separator = text.IndexOf(KeySeparator, StringComparison.Ordinal);
            return separator >= 0 ? text.Substring(0, separator) : text;
        }

        private static string Collapse(string text)
        {
            var builder = new StringBuilder(text.Length);
            var pendingSpace = false;

            foreach (var character in text)
            {
                if (char.IsWhiteSpace(character))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(character);
            }

            return builder.ToString();
        }

        /// <summary>
        /// One choice found in a line: where it stands, what it asks, and the
        /// wording the game draws for each outcome.
        /// </summary>
        private readonly struct Choice
        {
            public Choice(int start, int length, string code, IReadOnlyList<string> arms)
            {
                Start = start;
                Length = length;
                Code = code;
                Arms = arms;
            }

            public int Start { get; }

            public int Length { get; }

            /// <summary>What is being asked. Two sides asking differently cannot be paired.</summary>
            public string Code { get; }

            public IReadOnlyList<string> Arms { get; }

            /// <summary>The line as it reads once the game has picked this outcome.</summary>
            public string Draw(string text, int arm)
            {
                return text.Substring(0, Start) + Arms[arm] + text.Substring(Start + Length);
            }
        }

        private const string ArmOpen = "((";

        private const string ArmClose = "))";

        private const string ChoiceEnd = "/var>";

        /// <summary>
        /// Finds the first choice in the line whose shape can be read. A
        /// condition carrying a tail after its outcomes, or a bracket that
        /// never closes, is not one: those are left alone and the line goes
        /// where it went before rather than being half understood.
        /// </summary>
        private static bool TryReadChoice(string text, out Choice choice)
        {
            choice = default;

            var start = ChoiceStart.Match(text);
            while (start.Success)
            {
                if (TryReadArms(text, start.Index + start.Length, out var arms, out var end))
                {
                    choice = new Choice(start.Index, end - start.Index, start.Groups[1].Value, arms);
                    return true;
                }

                start = ChoiceStart.Match(text, start.Index + 1);
            }

            return false;
        }

        /// <summary>
        /// Reads the run of "((wording))" that follows a choice. Brackets are
        /// counted rather than searched for, because an outcome may hold a
        /// choice of its own and its brackets are not the end of this one.
        /// </summary>
        private static bool TryReadArms(string text, int at, out List<string> arms, out int end)
        {
            arms = new List<string>();
            end = 0;

            while (Has(text, at, ArmOpen))
            {
                var depth = 1;
                var from = at + ArmOpen.Length;
                var scan = from;

                while (scan < text.Length && depth > 0)
                {
                    if (Has(text, scan, ArmOpen))
                    {
                        depth++;
                        scan += ArmOpen.Length;
                    }
                    else if (Has(text, scan, ArmClose))
                    {
                        depth--;
                        if (depth == 0)
                        {
                            break;
                        }

                        scan += ArmClose.Length;
                    }
                    else
                    {
                        scan++;
                    }
                }

                if (depth > 0)
                {
                    return false;
                }

                arms.Add(text.Substring(from, scan - from));
                at = scan + ArmClose.Length;

                if (Has(text, at, " "))
                {
                    at++;
                }
            }

            if (arms.Count == 0 || !Has(text, at, ChoiceEnd))
            {
                return false;
            }

            end = at + ChoiceEnd.Length;
            return true;
        }

        private static bool Has(string text, int at, string token)
        {
            return at >= 0 && at + token.Length <= text.Length &&
                   string.CompareOrdinal(text, at, token, 0, token.Length) == 0;
        }

        private static int Count(string text, string needle)
        {
            var found = 0;
            var at = text.IndexOf(needle, StringComparison.Ordinal);
            while (at >= 0)
            {
                found++;
                at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
            }

            return found;
        }

        private static string FolderName(string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return string.Empty;
            }

            var separator = folder.LastIndexOf('/');
            return separator >= 0 ? folder.Substring(separator + 1) : folder;
        }

        /// <summary>
        /// The rows of one XLIFF file, in the order they are written.
        ///
        /// Read as XML rather than matched with an expression. The expression
        /// this replaces had already filed 1 281 rows' text under the wrong
        /// row's number, by running past a &lt;target/&gt; to the next row's
        /// target; it also dropped every row whose target carried an attribute
        /// it had not been told to expect. Neither is a mistake a reader can
        /// make: an element ends where it ends, and an attribute it does not
        /// know about is one it does not look at.
        ///
        /// Order is kept rather than handed back as a dictionary, because two
        /// rows can reduce to the same line and the first is the one kept.
        /// </summary>
        /// <param name="malformed">
        /// True when the file stopped being XML partway. What was read before
        /// that is still returned: half a sheet of hand-made translation beats
        /// none, and the count says how often it happens.
        /// </param>
        internal static List<Unit> ReadUnits(string xliff, out bool malformed)
        {
            var units = new List<Unit>();
            malformed = false;

            if (string.IsNullOrEmpty(xliff))
            {
                return units;
            }

            try
            {
                using var reader = XmlReader.Create(new StringReader(xliff), ReaderSettings);
                var id = string.Empty;

                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    if (reader.LocalName == "trans-unit")
                    {
                        id = reader.GetAttribute("id") ?? string.Empty;
                        continue;
                    }

                    if (reader.LocalName != "target" || id.Length == 0)
                    {
                        continue;
                    }

                    // Left out on purpose, and it used to be left out by
                    // accident: a state-qualifier says where the text came
                    // from, and the only one the export uses is
                    // "leveraged-mt" - a machine's work. This index exists to
                    // hold what people wrote instead.
                    if (reader.GetAttribute("state-qualifier") != null)
                    {
                        continue;
                    }

                    // A row nobody has translated, written <target/>.
                    if (reader.IsEmptyElement)
                    {
                        continue;
                    }

                    var text = ReadElementText(reader);
                    if (text.Length > 0)
                    {
                        units.Add(new Unit(id, text));
                    }
                }
            }
            catch (XmlException)
            {
                malformed = true;
            }

            return units;
        }

        /// <summary>
        /// The text of the element the reader is on, entities resolved.
        ///
        /// Written out rather than using ReadElementContentAsString, which
        /// throws if the element ever turns out to have a child. No target in
        /// the export has one today; a sheet that grew one should lose its
        /// markup, not throw away the words around it.
        /// </summary>
        private static string ReadElementText(XmlReader reader)
        {
            var text = new StringBuilder();
            var depth = reader.Depth;

            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.SignificantWhitespace:
                    case XmlNodeType.Whitespace:
                        text.Append(reader.Value);
                        break;

                    case XmlNodeType.EndElement when reader.Depth <= depth:
                        return text.ToString();
                }
            }

            return text.ToString();
        }
    }
}
