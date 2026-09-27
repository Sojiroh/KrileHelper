using Sharlayan.Core.Dialogue;

namespace KrileHelper.Tests.Sharlayan;

public sealed class DialogueTests
{
    [Fact]
    public void DecodeGameString_PreservesIconMarkAndUtf8()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("Привет ")
            .Concat(new byte[] { 0x02, 0x12, 0x02, 0x50, 0x03 })
            .Concat(System.Text.Encoding.UTF8.GetBytes(" мир"))
            .ToArray();

        var decoded = LiveDialogueReader.DecodeGameString(data, 0, data.Length);

        Assert.Equal("Привет " + GameIcons.Mark(79) + " мир", decoded);
        Assert.Equal(79, GameIcons.IdOf(decoded[7]));
    }

    [Fact]
    public void ChoiceBlock_RequiresEveryNumberExactlyOnce()
    {
        var block = "Вопрос\n2. Второй\n1) Первый";
        Assert.True(GameChoice.TryReadBlock(block, 2, out var question, out var answers));
        Assert.Equal("Вопрос", question);
        Assert.Equal(new[] { "Первый", "Второй" }, answers);
        Assert.False(GameChoice.TryReadBlock("Вопрос\n1. Один\n1. Снова", 2, out _, out _));
    }

    [Fact]
    public void CrossWorldName_IsProtectedOnlyWhenWorldIsGlued()
    {
        var worlds = new[] { "Louisoix", "Odin" };
        var hidden = CrossWorldNames.Hide("Cova RaeLouisoix met Odin", worlds, out var names);
        Assert.DoesNotContain("RaeLouisoix", hidden);
        Assert.Contains("Odin", hidden);
        Assert.Equal("Cova Rae" + GameIcons.Mark(CrossWorldNames.CrossWorldIcon) + "Louisoix met Odin", CrossWorldNames.Show(hidden, names));
    }

    [Fact]
    public void RecentUtterance_DistinguishesDifferentNamedSpeakers()
    {
        var recent = new RecentUtterance();
        var at = DateTime.UtcNow;
        Assert.False(recent.IsEcho("Understood.", "Cid", at));
        Assert.False(recent.IsEcho("Understood.", "Yda", at.AddMilliseconds(100)));
        Assert.True(recent.IsEcho("Understood.", "Cid", at.AddMilliseconds(200)));
    }
}
