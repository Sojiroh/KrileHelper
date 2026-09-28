using System.Text.Json;
using Avalonia.Media;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;
using KrileHelper.UI.ViewModels;
using Sharlayan.Core.ChatLog;

namespace KrileHelper.Tests.UI;

public sealed class ChatTabsTests
{
    [Fact]
    public void OverlappingTabsShareDisplayAndTranslationState()
    {
        using var fixture = new Fixture([
            new ChatTabSettings { Id = "one", Name = "One", Channels = ["say"] },
            new ChatTabSettings { Id = "two", Name = "Two", Channels = ["say"] },
        ]);
        var display = fixture.Line("say");

        fixture.Tabs.Add(display);

        Assert.Same(display, fixture.Tabs.Tabs[0].Lines[0]);
        Assert.Same(display, fixture.Tabs.Tabs[1].Lines[0]);
        display.Translation = "translated";
        display.TranslationPending = false;
        Assert.Equal("translated", fixture.Tabs.Tabs[0].Lines[0].Translation);
        Assert.False(fixture.Tabs.Tabs[1].Lines[0].TranslationPending);
    }

    [Fact]
    public void MessageOnlyAppearsInMatchingTabWhileHistoryKeepsItForLaterFilters()
    {
        using var fixture = new Fixture([
            new ChatTabSettings { Id = "say", Name = "Say", Channels = ["say"] },
            new ChatTabSettings { Id = "party", Name = "Party", Channels = ["party"] },
        ]);
        var display = fixture.Line("party");

        fixture.Tabs.Add(display);

        Assert.Empty(fixture.Tabs.Tabs[0].Lines);
        Assert.Single(fixture.Tabs.Tabs[1].Lines);
        Assert.Same(display, fixture.Tabs.History[0]);
    }

    [Fact]
    public void SelectionIsPersistedAndRemovedSelectionFallsBack()
    {
        using var fixture = new Fixture([
            new ChatTabSettings { Id = "one", Name = "One", IncludeAllChannels = true },
            new ChatTabSettings { Id = "two", Name = "Two", IncludeAllChannels = true },
        ]);
        fixture.Tabs.SelectedTab = fixture.Tabs.Tabs[1];
        Assert.Equal("two", fixture.Settings.Current.SelectedChatTabId);
        var persisted = JsonSerializer.Deserialize<AppSettings>(
            File.ReadAllText(fixture.Settings.FilePath))!;
        Assert.Equal("two", persisted.SelectedChatTabId);

        fixture.Settings.Current.ChatTabs.RemoveAt(1);
        fixture.Settings.NotifyChanged();

        Assert.Equal("one", fixture.Settings.Current.SelectedChatTabId);
        Assert.Equal("one", fixture.Tabs.SelectedTab?.Id);
        Assert.Single(fixture.Tabs.Tabs);
        persisted = JsonSerializer.Deserialize<AppSettings>(
            File.ReadAllText(fixture.Settings.FilePath))!;
        Assert.Equal("one", persisted.SelectedChatTabId);
    }

    [Fact]
    public void FilterChangesRebuildRetainedRowsWithoutReplacingTabOrDisplays()
    {
        using var fixture = new Fixture([
            new ChatTabSettings { Id = "tab", Name = "Tab", Channels = ["say"] },
        ]);
        var say = fixture.Line("say");
        var party = fixture.Line("party");
        fixture.Tabs.Add(say);
        fixture.Tabs.Add(party);
        var tab = fixture.Tabs.Tabs[0];
        var lines = tab.Lines;

        fixture.Settings.Current.ChatTabs[0].Channels = ["party"];
        fixture.Settings.NotifyChanged();

        Assert.Same(tab, fixture.Tabs.Tabs[0]);
        Assert.Same(lines, tab.Lines);
        Assert.Single(tab.Lines);
        Assert.Same(party, tab.Lines[0]);
    }

    [Fact]
    public void EvictionDropsExactlyTheOldestAtTheRetentionBoundary()
    {
        using var fixture = new Fixture([
            new ChatTabSettings { Id = "all", Name = "All", IncludeAllChannels = true },
        ]);
        var displays = Enumerable.Range(0, ChatTabsViewModel.MaxRetainedLines + 1)
            .Select(i => fixture.Line($"code-{i}"))
            .ToArray();

        foreach (var display in displays)
            fixture.Tabs.Add(display);

        var tab = fixture.Tabs.Tabs[0];
        Assert.Equal(ChatTabsViewModel.MaxRetainedLines, fixture.Tabs.History.Count);
        Assert.Equal(ChatTabsViewModel.MaxRetainedLines, tab.Lines.Count);
        Assert.DoesNotContain(displays[0], fixture.Tabs.History);
        Assert.DoesNotContain(displays[0], tab.Lines);
        Assert.Same(displays[1], fixture.Tabs.History[0]);
        Assert.Same(displays[^1], fixture.Tabs.History[^1]);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory;
        public SettingsService Settings { get; }
        public ChatTabsViewModel Tabs { get; }

        public Fixture(List<ChatTabSettings> tabs)
        {
            _directory = Path.Combine(Path.GetTempPath(), $"krile-chat-tabs-{Guid.NewGuid():N}");
            Settings = new SettingsService(Path.Combine(_directory, "settings.json"));
            Settings.Current.ChatTabs = tabs;
            Settings.Current.SelectedChatTabId = tabs[0].Id;
            Tabs = new ChatTabsViewModel(Settings);
        }

        public ChatLineDisplay Line(string code) => ChatLineDisplay.Create(
            new ChatLogItem { TimeStamp = DateTime.UtcNow, Code = code, Line = $"line-{code}" },
            new ChatCodeInfo(code, code, Brushes.White, 1));

        public void Dispose()
        {
            Tabs.Dispose();
            try { Directory.Delete(_directory, recursive: true); } catch { }
        }
    }
}
