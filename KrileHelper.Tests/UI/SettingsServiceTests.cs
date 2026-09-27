using Avalonia.Media;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;

namespace KrileHelper.Tests.UI;

public sealed class SettingsServiceTests
{
    [Fact]
    public void MigratesDeepLKeyWithoutLosingLanguageOrChannelPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(path, """
                {"Version":1,"Translation":{"Engine":"DeepL","DeepLApiKey":"legacy:fx","TargetLanguage":"ja"},
                 "Channels":{"000D":{"Show":true,"Translate":false}}}
                """);
            var service = new SettingsService(path);
            Assert.Equal("legacy:fx", service.Current.Translation.GetProvider().ApiKey);
            Assert.Equal("ja", service.Current.Translation.TargetLanguage);
            Assert.False(service.Current.Channels["000D"].Translate);
            var reloaded = new SettingsService(path);
            Assert.Equal("legacy:fx", reloaded.Current.Translation.GetProvider().ApiKey);
            if (OperatingSystem.IsLinux())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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
