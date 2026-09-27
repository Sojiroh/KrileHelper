using System.Text;
using Sharlayan.Core.ChatLog;
using Sharlayan.Core.Dialogue;
using Sharlayan.Core.Scanning;

namespace KrileHelper.Tests.Sharlayan;

public sealed class LiveDialogueReaderTests
{
    [Fact]
    public void Talk_FollowsLegacyAnchorAndInlineUnitList_ThenTracksVisibilityAndSpeaker()
    {
        var game = new GameMemory();
        game.Talk("Cid", "Understood.");
        var first = game.Reader.Poll();
        Assert.True(first.SourceAvailable);
        Assert.Equal(DialogueSurface.Window, first.Surface);
        Assert.Single(first.Lines);
        Assert.Equal("Cid", first.Speaker);
        Assert.Equal("Understood.", first.Text);
        Assert.Equal(640, first.Bounds.Width);
        Assert.Empty(game.Reader.Poll().Lines);
        Assert.True(game.Reader.ShouldSuppressChat(new ChatLogItem { Code = "003D", Line = "Cid: Understood." }));
        Assert.False(game.Reader.ShouldSuppressChat(new ChatLogItem { Code = "000A", Line = "Cid: Understood." }));

        game.Talk("Yda", "Understood.");
        var nextSpeaker = game.Reader.Poll();
        Assert.Single(nextSpeaker.Lines);
        Assert.Equal("Yda", nextSpeaker.Speaker);
        game.Visible(false);
        Assert.False(game.Reader.Poll().IsVisible);
        game.Talk("Yda", "Let us go.");
        Assert.Empty(game.Reader.Poll().Lines);
        game.Visible(true);
        var reopened = game.Reader.Poll();
        Assert.Single(reopened.Lines);
        Assert.Equal("Let us go.", reopened.Text);
    }

    [Fact]
    public void SubtitleAndChoices_KeepNumberedRowsAndClearClosedSurface()
    {
        var game = new GameMemory();
        game.Subtitle("Meanwhile, in Ul'dah...");
        var subtitle = game.Reader.Poll();
        Assert.Equal(DialogueSurface.Subtitle, subtitle.Surface);
        Assert.Equal("Meanwhile, in Ul'dah...", subtitle.Text);
        Assert.Equal("0044", Assert.Single(subtitle.Lines).Code);

        game.Choice("What will you do?", "Stay here.", "Set forth.");
        var choice = game.Reader.Poll();
        Assert.Equal(DialogueSurface.Choice, choice.Surface);
        Assert.Equal("What will you do?\n1. Stay here.\n2. Set forth.", choice.Text.Replace("\r\n", "\n"));
        Assert.Equal(new[] { "Stay here.", "Set forth." }, choice.Choice!.Answers);
        Assert.Equal(2, choice.Choice.AnswerBounds.Count);
        Assert.Empty(game.Reader.Poll().Lines);
        game.Visible(false);
        Assert.False(game.Reader.Poll().IsVisible);
    }

    [Fact]
    public void Bubbles_AnnounceNewTextInsteadOfLongerUnchangedText()
    {
        var game = new GameMemory();
        game.Bubbles("A much longer sentence from a passer-by.", "");
        Assert.Equal("A much longer sentence from a passer-by.", Assert.Single(game.Reader.Poll().Lines).Line);
        game.Bubbles("A much longer sentence from a passer-by.", "Hello!");
        var snapshot = game.Reader.Poll();
        Assert.Equal(DialogueSurface.Bubble, snapshot.Surface);
        Assert.Equal("Hello!", Assert.Single(snapshot.Lines).Line);
        Assert.Empty(game.Reader.Poll().Lines);
    }

    [Fact]
    public void AmbientBubble_DoesNotReplaceOrStrandAnOpenDialogue()
    {
        var game = new GameMemory();
        game.Talk("Cid", "Understood.");
        Assert.Equal(DialogueSurface.Window, game.Reader.Poll().Surface);

        game.BackgroundBubble("Welcome to the Gold Saucer!");
        var simultaneous = game.Reader.Poll();
        Assert.Equal(DialogueSurface.Window, simultaneous.Surface);
        Assert.Equal("Understood.", simultaneous.Text);
        Assert.Contains(simultaneous.Lines, line => line.Line == "Welcome to the Gold Saucer!");

        // The next sweep consumes the bubble, but the same Talk panel is still open.
        var afterBubble = game.Reader.Poll();
        Assert.True(afterBubble.IsVisible);
        Assert.Equal(DialogueSurface.Window, afterBubble.Surface);
        Assert.Equal("Cid", afterBubble.Speaker);
        Assert.Equal("Understood.", afterBubble.Text);
        Assert.Empty(afterBubble.Lines);

        game.Talk("Cid", "Let us go.");
        Assert.Equal("Let us go.", game.Reader.Poll().Text);
    }

    [Fact]
    public void IdenticalDialogueAtReplacementAddon_ReacquiresOverlayWithoutDuplicatingChat()
    {
        var game = new GameMemory();
        game.Talk("Cid", "Understood.");
        Assert.Single(game.Reader.Poll().Lines);

        // The old addon can be replaced between polls. Content deduplication must
        // not pin presentation to the old memory address.
        game.ReplaceTalkAddon();
        var reopened = game.Reader.Poll();
        Assert.True(reopened.IsVisible);
        Assert.Equal(DialogueSurface.Window, reopened.Surface);
        Assert.Equal("Cid", reopened.Speaker);
        Assert.Equal("Understood.", reopened.Text);
        Assert.True(reopened.Bounds.IsKnown);
        Assert.Empty(reopened.Lines);
        Assert.True(game.Reader.Poll().IsVisible);
    }

    [Fact]
    public void IdenticalDialogueAfterHiddenSweep_ReopensWithoutDuplicatingChat()
    {
        var game = new GameMemory();
        game.Talk("Cid", "Understood.");
        Assert.Single(game.Reader.Poll().Lines);
        game.Visible(false);
        Assert.False(game.Reader.Poll().IsVisible);
        game.Visible(true);
        var reopened = game.Reader.Poll();
        Assert.True(reopened.IsVisible);
        Assert.Equal("Understood.", reopened.Text);
        Assert.Empty(reopened.Lines);
    }

    // Real x64 structure offsets, a shifted RaptureAtkModule, and the legacy chat-map
    // displacement. No alternate reader/layout implementation is exercised here.
    private sealed class GameMemory
    {
        private const ulong Ui = 0x10000000;
        private const ulong Manager = 0x20000000;
        private const ulong Addon = 0x30000000;
        private const ulong Root = Addon + 0x1000;
        private const ulong Nodes = Addon + 0x2000;
        private const ulong Speaker = Addon + 0x3000;
        private const ulong Body = Addon + 0x4000;
        private const ulong Third = Addon + 0x5000;
        private readonly FakeNativeMemory _memory = new();
        private readonly byte[] _addon = new byte[0x500];
        private readonly byte[] _root = Node(0, 0, "", 100, 600, 640, 160);
        public LiveDialogueReader Reader { get; }

        public GameMemory()
        {
            _memory.WriteUInt64(Ui + 0xD2690 + 0x2C0, Manager);
            _memory.Write(Manager + 0x6900 + 0x808, BitConverter.GetBytes((ushort)1));
            _memory.WriteUInt64(Manager + 0x6900 + 8, Addon);
            var signature = new Signature("CHATLOG", "AA") { SigScanAddress = Ui + 0x1AC0 + 0x14 };
            Reader = new LiveDialogueReader(_memory, 1, signature, DialogueMemoryLayout.Default);
            Visible(true);
        }

        public void Visible(bool visible)
        {
            Put(_root, 0xAE, BitConverter.GetBytes((ushort)(visible ? 0x10 : 0)));
            _memory.Write(Root, _root);
        }

        public void Talk(string speaker, string body)
        {
            Prepare("Talk");
            Put(_addon, 0x238, BitConverter.GetBytes(Speaker));
            Put(_addon, 0x240, BitConverter.GetBytes(Body));
            TextNodes(Node(2, 3, speaker), Node(3, 3, body), Node(4, 4, ""));
            _memory.Write(Addon, _addon);
        }

        public void ReplaceTalkAddon()
        {
            const ulong replacement = Addon + 0x6000;
            _memory.Write(replacement, _addon);
            _memory.WriteUInt64(Manager + 0x6900 + 8, replacement);
        }

        public void Subtitle(string text)
        {
            Prepare("TalkSubtitle");
            Utf8(_addon, 0x238, text);
            _memory.Write(Addon, _addon);
        }

        public void Choice(string question, string first, string second)
        {
            Prepare("CutSceneSelectString");
            TextNodes(Node(1, 3, question, 110, 610), Node(2, 3, first, 110, 650), Node(3, 3, second, 110, 690));
            _memory.Write(Addon, _addon);
        }

        public void Bubbles(string first, string second)
        {
            Prepare("_MiniTalk");
            Put(_addon, 0x248 + 0x20, BitConverter.GetBytes(Speaker));
            Put(_addon, 0x248 + 0x38 + 0x20, BitConverter.GetBytes(Body));
            _memory.Write(Speaker, Node(1, 3, first));
            _memory.Write(Body, Node(2, 3, second));
            _memory.Write(Addon, _addon);
        }

        public void BackgroundBubble(string text)
        {
            const ulong bubble = Addon + 0x10000;
            var addon = new byte[0x500];
            Put(addon, 8, Encoding.ASCII.GetBytes("_MiniTalk"));
            Put(addon, 0xC8, BitConverter.GetBytes(bubble + 0x1000));
            Put(addon, 0x248 + 0x20, BitConverter.GetBytes(bubble + 0x2000));
            _memory.Write(bubble, addon);
            _memory.Write(bubble + 0x1000, Node(0, 0, ""));
            _memory.Write(bubble + 0x2000, Node(1, 3, text));
            _memory.WriteUInt64(Manager + 0x6900 + 16, bubble);
            _memory.Write(Manager + 0x6900 + 0x808, BitConverter.GetBytes((ushort)2));
        }

        private void Prepare(string name)
        {
            Array.Clear(_addon);
            Put(_addon, 8, Encoding.ASCII.GetBytes(name));
            Put(_addon, 0xC8, BitConverter.GetBytes(Root));
        }

        private void TextNodes(byte[] speaker, byte[] body, byte[] third)
        {
            Put(_addon, 0x28 + 0x42, BitConverter.GetBytes((ushort)3));
            Put(_addon, 0x28 + 0x50, BitConverter.GetBytes(Nodes));
            _memory.WriteUInt64(Nodes, Speaker);
            _memory.WriteUInt64(Nodes + 8, Body);
            _memory.WriteUInt64(Nodes + 16, Third);
            _memory.Write(Speaker, speaker);
            _memory.Write(Body, body);
            _memory.Write(Third, third);
        }

        private static byte[] Node(uint id, ushort type, string text, float x = 110, float y = 620,
            ushort width = 600, ushort height = 30)
        {
            var bytes = new byte[0x180];
            Put(bytes, 8, BitConverter.GetBytes(id));
            Put(bytes, 0x40, BitConverter.GetBytes(type));
            Put(bytes, 0x4C, BitConverter.GetBytes(1f));
            Put(bytes, 0x70, BitConverter.GetBytes(x));
            Put(bytes, 0x74, BitConverter.GetBytes(y));
            Put(bytes, 0xA0, BitConverter.GetBytes(width));
            Put(bytes, 0xA2, BitConverter.GetBytes(height));
            Put(bytes, 0xAE, BitConverter.GetBytes((ushort)0x10));
            Utf8(bytes, 0xD0, text);
            return bytes;
        }

        private static void Utf8(byte[] bytes, int offset, string text)
        {
            var encoded = Encoding.UTF8.GetBytes(text + "\0");
            Put(bytes, offset + 16, BitConverter.GetBytes((long)encoded.Length));
            bytes[offset + 33] = 1;
            Put(bytes, offset + 34, encoded);
        }

        private static void Put(byte[] target, int offset, byte[] value) => value.CopyTo(target, offset);
    }
}
