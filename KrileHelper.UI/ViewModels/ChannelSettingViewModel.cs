using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;

namespace KrileHelper.UI.ViewModels;

public partial class ChannelSettingViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ChannelSetting _defaultSetting;

    public string Code { get; }
    public string Name { get; }
    public IBrush Color { get; }

    [ObservableProperty] private bool _show;
    [ObservableProperty] private bool _translate;

    public ChannelSettingViewModel(ChatCodeInfo info, SettingsService settings)
    {
        Code = info.Code;
        Name = info.Name;
        Color = info.Color;
        _settings = settings;
        _defaultSetting = SettingsService.GetDefaultChannel(info);

        var s = settings.GetChannel(info);
        _show = s.Show;
        _translate = s.Translate;
    }

    partial void OnShowChanged(bool value) => Persist();
    partial void OnTranslateChanged(bool value) => Persist();

    private void Persist()
    {
        // Only store non-default values to keep settings.json lean.
        if (Show == _defaultSetting.Show && Translate == _defaultSetting.Translate)
            _settings.Current.Channels.Remove(Code);
        else
            _settings.Current.Channels[Code] = new ChannelSetting { Show = Show, Translate = Translate };
        _settings.NotifyChanged();
    }
}
