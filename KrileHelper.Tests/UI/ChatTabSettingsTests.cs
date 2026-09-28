using Avalonia.Media;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;
using KrileHelper.UI.ViewModels;

namespace KrileHelper.Tests.UI;

public sealed class ChatTabSettingsTests
{
    [Fact]
    public void AddRenameAndSelectChannels_RoundTripsWithStableId()
    {
        var path = TemporaryPath();
        try
        {
            var settings = new SettingsService(path);
            var editor = new ChatTabEditorViewModel(settings, Registry());
            var originalId = editor.SelectedTab!.Id;

            editor.AddTabCommand.Execute(null);
            var custom = editor.SelectedTab!;
            var customId = custom.Id;
            custom.Name = "  Party and FC  ";
            editor.Channels.Single(channel => channel.Code == "000E").IsSelected = true;
            editor.Channels.Single(channel => channel.Code == "0018").IsSelected = true;

            var reloaded = new SettingsService(path);
            var saved = Assert.Single(reloaded.Current.ChatTabs, tab => tab.Id == customId);
            Assert.Equal("Party and FC", saved.Name);
            Assert.Equal(new[] { "000E", "0018" }, saved.Channels);
            Assert.Contains(reloaded.Current.ChatTabs, tab => tab.Id == originalId);
            var reopened = new ChatTabEditorViewModel(reloaded, Registry());
            Assert.Equal(customId, reopened.Tabs.Single(tab => tab.Name == "Party and FC").Id);
            reopened.SelectedTab = reopened.Tabs.Single(tab => tab.Id == originalId);
            reopened.Channels.Single(channel => channel.Code == "000E").IsSelected = true;

            var overlap = new SettingsService(path).Current.ChatTabs.Single(tab => tab.Id == originalId);
            Assert.Contains("000E", overlap.Channels);
            Assert.Contains("000E", new SettingsService(path).Current.ChatTabs.Single(tab => tab.Id == customId).Channels);
            Assert.Equal(customId, reopened.Tabs.Single(tab => tab.Name == "Party and FC").Id);
        }
        finally
        {
            DeleteTemporaryPath(path);
        }
    }

    [Fact]
    public void DeletingActiveTabSelectsFallbackBeforeChangedNotification()
    {
        var path = TemporaryPath();
        try
        {
            var settings = new SettingsService(path);
            var editor = new ChatTabEditorViewModel(settings, Registry());
            editor.AddTabCommand.Execute(null);
            var activeId = editor.SelectedTab!.Id;
            settings.Current.SelectedChatTabId = activeId;
            var observed = false;
            settings.Changed += (_, _) =>
            {
                observed = true;
                Assert.Contains(settings.Current.ChatTabs, tab => tab.Id == settings.Current.SelectedChatTabId);
            };

            editor.DeleteTabCommand.Execute(null);

            Assert.True(observed);
            Assert.NotEqual(activeId, settings.Current.SelectedChatTabId);
            Assert.Single(settings.Current.ChatTabs);
        }
        finally
        {
            DeleteTemporaryPath(path);
        }
    }

    [Fact]
    public void LastTabCannotBeDeleted_AndBlankNameNeverPersists()
    {
        var path = TemporaryPath();
        try
        {
            var settings = new SettingsService(path);
            var editor = new ChatTabEditorViewModel(settings, Registry());
            var tab = editor.SelectedTab!;

            tab.Name = "Keep me";
            tab.Name = "";
            Assert.Equal("", tab.Name);
            Assert.False(editor.DeleteTabCommand.CanExecute(null));
            editor.DeleteTabCommand.Execute(null);

            var reloaded = new SettingsService(path);
            var saved = Assert.Single(reloaded.Current.ChatTabs);
            Assert.Equal("Keep me", saved.Name);
        }
        finally
        {
            DeleteTemporaryPath(path);
        }
    }

    [Fact]
    public void EditingTabsDoesNotChangeOverlaySelectionOrGlobalPrivacySettings()
    {
        var path = TemporaryPath();
        try
        {
            var settings = new SettingsService(path);
            settings.Current.SelectedChatTabId = "all";
            settings.Current.Channels["000D"] = new ChannelSetting { Show = false, Translate = false };
            var before = settings.Current.Channels["000D"];
            var editor = new ChatTabEditorViewModel(settings, Registry());

            editor.AddTabCommand.Execute(null);
            editor.SelectedTab!.IncludeAllChannels = false;
            editor.Channels.Single(channel => channel.Code == "000E").IsSelected = true;

            Assert.Equal("all", settings.Current.SelectedChatTabId);
            Assert.Same(before, settings.Current.Channels["000D"]);
            Assert.False(settings.Current.Channels["000D"].Show);
            Assert.False(settings.Current.Channels["000D"].Translate);
        }
        finally
        {
            DeleteTemporaryPath(path);
        }
    }

    private static ChatCodeRegistry Registry() => new(new[]
    {
        new ChatCodeInfo("000D", "Tell", Brushes.Gold, 1),
        new ChatCodeInfo("000E", "Party", Brushes.CornflowerBlue, 1),
        new ChatCodeInfo("0018", "FreeCompany", Brushes.MediumPurple, 1),
    });

    private static string TemporaryPath() => Path.Combine(
        Path.GetTempPath(), "krile-helper-tests", Guid.NewGuid().ToString("N"), "settings.json");

    private static void DeleteTemporaryPath(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
