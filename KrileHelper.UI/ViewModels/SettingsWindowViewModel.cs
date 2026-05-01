using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;

namespace KrileHelper.UI.ViewModels;

public partial class SettingsWindowViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private bool _suppressPersist;

    public IReadOnlyList<LanguageOption> SourceOptions => LanguageOptions.Sources;
    public IReadOnlyList<LanguageOption> TargetOptions => LanguageOptions.Targets;
    public IReadOnlyList<EngineOption> EngineOptions => TranslatorFactory.Available;
    public ObservableCollection<ChannelSettingViewModel> Channels { get; } = new();

    [ObservableProperty] private EngineOption _selectedEngine;
    [ObservableProperty] private LanguageOption _selectedSource;
    [ObservableProperty] private LanguageOption _selectedTarget;
    [ObservableProperty] private string _deepLApiKey;
    [ObservableProperty] private double _backgroundOpacity;
    [ObservableProperty] private bool _topmost;

    public bool IsDeepL => SelectedEngine?.Id == TranslatorFactory.DeepL;

    public SettingsWindowViewModel(SettingsService settings, ChatCodeRegistry registry)
    {
        _settings = settings;

        _suppressPersist = true;
        _selectedEngine = TranslatorFactory.Available.FirstOrDefault(e => e.Id == settings.Current.Translation.Engine)
                          ?? TranslatorFactory.Available[0];
        _selectedSource = LanguageOptions.Sources.FirstOrDefault(l => l.Code == settings.Current.Translation.SourceLanguage)
                          ?? LanguageOptions.Sources[0];
        _selectedTarget = LanguageOptions.Targets.FirstOrDefault(l => l.Code == settings.Current.Translation.TargetLanguage)
                          ?? LanguageOptions.Targets[0];
        _deepLApiKey = settings.Current.Translation.DeepLApiKey;
        _backgroundOpacity = settings.Current.Window.BackgroundOpacity;
        _topmost = settings.Current.Window.Topmost;
        _suppressPersist = false;

        foreach (var info in registry.ByCode.Values.OrderBy(c => c.Code))
            Channels.Add(new ChannelSettingViewModel(info, settings));
    }

    partial void OnSelectedEngineChanged(EngineOption value)
    {
        OnPropertyChanged(nameof(IsDeepL));
        if (_suppressPersist) return;
        _settings.Current.Translation.Engine = value.Id;
        _settings.NotifyChanged();
    }

    partial void OnDeepLApiKeyChanged(string value)
    {
        if (_suppressPersist) return;
        _settings.Current.Translation.DeepLApiKey = value?.Trim() ?? "";
        _settings.NotifyChanged();
    }

    partial void OnSelectedSourceChanged(LanguageOption value)
    {
        if (_suppressPersist) return;
        _settings.Current.Translation.SourceLanguage = value.Code;
        _settings.NotifyChanged();
    }

    partial void OnSelectedTargetChanged(LanguageOption value)
    {
        if (_suppressPersist) return;
        _settings.Current.Translation.TargetLanguage = value.Code;
        _settings.NotifyChanged();
    }

    partial void OnBackgroundOpacityChanged(double value)
    {
        if (_suppressPersist) return;
        _settings.Current.Window.BackgroundOpacity = value;
        _settings.NotifyChanged();
    }

    partial void OnTopmostChanged(bool value)
    {
        if (_suppressPersist) return;
        _settings.Current.Window.Topmost = value;
        _settings.NotifyChanged();
    }
}
