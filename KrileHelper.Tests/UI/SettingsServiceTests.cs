using Avalonia.Media;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;

namespace KrileHelper.Tests.UI;

public sealed class SettingsServiceTests
{
    [Fact]
    public void GetChannel_UsesBundledDefaultWhenChannelWasNotCustomized()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var service = new SettingsService(path);
        var info = new ChatCodeInfo("000D", "Tell", Brushes.White, MsgType: 3, TranslateByDefault: false);

        var setting = service.GetChannel(info);

        Assert.True(setting.Show);
        Assert.False(setting.Translate);
    }

    [Fact]
    public void GetChannel_ReturnsPersistedOverrideWhenPresent()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var service = new SettingsService(path);
        var info = new ChatCodeInfo("000D", "Tell", Brushes.White, MsgType: 3, TranslateByDefault: false);
        service.Current.Channels[info.Code] = new ChannelSetting { Show = true, Translate = true };

        var setting = service.GetChannel(info);

        Assert.True(setting.Show);
        Assert.True(setting.Translate);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var saved = new SettingsService(path);
        saved.Current.Translation.TargetLanguage = "ja";
        saved.Current.Window.Width = 700;
        saved.Save();

        var loaded = new SettingsService(path);

        Assert.Equal("ja", loaded.Current.Translation.TargetLanguage);
        Assert.Equal(700, loaded.Current.Window.Width);
    }

    [Fact]
    public void Load_CorruptSettingsBacksUpFileAndUsesDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, "{ nope");

        var service = new SettingsService(path);

        Assert.Equal("GoogleFree", service.Current.Translation.Engine);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".bad"));
    }
}
