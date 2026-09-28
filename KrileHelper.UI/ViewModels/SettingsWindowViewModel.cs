using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;
using Translation.Core;
using Translation.Core.Reference;

namespace KrileHelper.UI.ViewModels;

public partial class SettingsWindowViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly ReferenceTranslationService _reference;
    private bool _suppressPersist;

    public IReadOnlyList<LanguageOption> SourceOptions => LanguageOptions.Sources;
    public IReadOnlyList<LanguageOption> TargetOptions => LanguageOptions.Targets;
    public IReadOnlyList<LanguageOption> GameLanguageOptions { get; } = LanguageOptions.Sources
        .Where(language => language.Code is "en" or "de" or "fr" or "ja").ToArray();
    public IReadOnlyList<EngineOption> EngineOptions => TranslatorFactory.Available;
    public ObservableCollection<ChannelSettingViewModel> Channels { get; } = new();
    public ChatTabEditorViewModel TabEditor { get; }
    [ObservableProperty] private EngineOption _selectedEngine;
    [ObservableProperty] private LanguageOption _selectedSource;
    [ObservableProperty] private LanguageOption _selectedTarget;
    [ObservableProperty] private LanguageOption _selectedGameLanguage;
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _endpoint = "";
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _region = "";
    [ObservableProperty] private double _backgroundOpacity;
    [ObservableProperty] private bool _topmost;
    [ObservableProperty] private bool _translateNpcNames;
    [ObservableProperty] private bool _translatePlayerNames;
    [ObservableProperty] private bool _liveDialogueEnabled;
    [ObservableProperty] private bool _dialogueOverlayEnabled;
    [ObservableProperty] private bool _referenceEnabled;
    [ObservableProperty] private string _referenceStatus = "";

    public bool SupportsApiKey => TranslationEngines.All.First(e => e.Id == SelectedEngine.Id).SupportsApiKey;
    public bool SupportsEndpoint => TranslationEngines.All.First(e => e.Id == SelectedEngine.Id).SupportsEndpoint;
    public bool SupportsModel => TranslationEngines.All.First(e => e.Id == SelectedEngine.Id).SupportsModel;
    public bool SupportsRegion => TranslationEngines.All.First(e => e.Id == SelectedEngine.Id).SupportsRegion;
    public string EngineHelp => TranslationEngines.All.First(e => e.Id == SelectedEngine.Id).Help;
    public string EndpointHint => TranslationEngines.All.First(e => e.Id == SelectedEngine.Id).DefaultEndpoint;
    public string ModelHint => TranslationEngines.All.First(e => e.Id == SelectedEngine.Id).DefaultModel;

    public SettingsWindowViewModel(SettingsService settings, ChatCodeRegistry registry, ReferenceTranslationService reference)
    {
        _settings = settings;
        _reference = reference;
        _suppressPersist = true;
        _selectedEngine = EngineOptions.FirstOrDefault(e => e.Id == settings.Current.Translation.Engine) ?? EngineOptions[0];
        _selectedSource = SourceOptions.FirstOrDefault(l => l.Code == settings.Current.Translation.SourceLanguage) ?? SourceOptions[0];
        _selectedTarget = TargetOptions.FirstOrDefault(l => l.Code == settings.Current.Translation.TargetLanguage) ?? TargetOptions[0];
        _selectedGameLanguage = GameLanguageOptions.FirstOrDefault(l => l.Code == settings.Current.Reference.GameLanguage) ?? GameLanguageOptions[0];
        _backgroundOpacity = settings.Current.Window.BackgroundOpacity;
        _topmost = settings.Current.Window.Topmost;
        _translateNpcNames = settings.Current.Translation.TranslateNpcNames;
        _translatePlayerNames = settings.Current.Translation.TranslatePlayerNames;
        _liveDialogueEnabled = settings.Current.Dialogue.Enabled;
        _dialogueOverlayEnabled = settings.Current.Dialogue.OverlayEnabled;
        _referenceEnabled = settings.Current.Reference.Enabled;
        TabEditor = new ChatTabEditorViewModel(settings, registry);
        LoadProvider();
        RefreshReferenceStatus();
        _suppressPersist = false;
        foreach (var info in registry.ByCode.Values.OrderBy(c => c.Code))
            Channels.Add(new ChannelSettingViewModel(info, settings));
    }
 

    private void LoadProvider()
    {
        var previous = _suppressPersist;
        _suppressPersist = true;
        var provider = _settings.Current.Translation.Providers.GetValueOrDefault(SelectedEngine.Id) ?? new ProviderSettings();
        ApiKey = provider.ApiKey;
        Endpoint = provider.Endpoint;
        Model = provider.Model;
        Region = provider.Region;
        _suppressPersist = previous;
        OnPropertyChanged(nameof(SupportsApiKey));
        OnPropertyChanged(nameof(SupportsEndpoint));
        OnPropertyChanged(nameof(SupportsModel));
        OnPropertyChanged(nameof(SupportsRegion));
        OnPropertyChanged(nameof(EngineHelp));
        OnPropertyChanged(nameof(EndpointHint));
        OnPropertyChanged(nameof(ModelHint));
    }

    partial void OnSelectedEngineChanged(EngineOption value)
    {
        if (_suppressPersist) return;
        _settings.Current.Translation.Engine = value.Id;
        LoadProvider();
        Persist();
    }

    private void PersistProvider()
    {
        if (_suppressPersist) return;
        _settings.Current.Translation.Providers[SelectedEngine.Id] = new ProviderSettings
        {
            ApiKey = ApiKey.Trim(), Endpoint = Endpoint.Trim(), Model = Model.Trim(), Region = Region.Trim(),
        };
        Persist();
    }

    private void Persist()
    {
        if (!_suppressPersist) _settings.NotifyChanged();
    }

    partial void OnApiKeyChanged(string value) => PersistProvider();
    partial void OnEndpointChanged(string value) => PersistProvider();
    partial void OnModelChanged(string value) => PersistProvider();
    partial void OnRegionChanged(string value) => PersistProvider();
    partial void OnSelectedSourceChanged(LanguageOption value)
    {
        _settings.Current.Translation.SourceLanguage = value.Code;
        Persist();
    }
    partial void OnSelectedTargetChanged(LanguageOption value)
    {
        _settings.Current.Translation.TargetLanguage = value.Code;
        Persist();
    }
    partial void OnSelectedGameLanguageChanged(LanguageOption value)
    {
        _settings.Current.Reference.GameLanguage = value.Code;
        Persist();
    }
    partial void OnBackgroundOpacityChanged(double value)
    {
        _settings.Current.Window.BackgroundOpacity = value;
        Persist();
    }
    partial void OnTopmostChanged(bool value)
    {
        _settings.Current.Window.Topmost = value;
        Persist();
    }
    partial void OnTranslateNpcNamesChanged(bool value)
    {
        _settings.Current.Translation.TranslateNpcNames = value;
        Persist();
    }
    partial void OnTranslatePlayerNamesChanged(bool value)
    {
        _settings.Current.Translation.TranslatePlayerNames = value;
        Persist();
    }
    partial void OnLiveDialogueEnabledChanged(bool value)
    {
        _settings.Current.Dialogue.Enabled = value;
        Persist();
    }
    partial void OnDialogueOverlayEnabledChanged(bool value)
    {
        _settings.Current.Dialogue.OverlayEnabled = value;
        Persist();
    }
    partial void OnReferenceEnabledChanged(bool value)
    {
        _settings.Current.Reference.Enabled = value;
        Persist();
    }

    private void RefreshReferenceStatus() => ReferenceStatus = _reference.IsAvailable
        ? $"{_reference.LineCount:N0} lines · {_reference.SourceLanguageCode} → {_reference.LanguageCode}"
        : "Not installed. Download only if you want hand-written Russian translations.";

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task UpdateReferenceAsync(CancellationToken cancellationToken)
    {
        ReferenceStatus = "Checking XIV Rus Translation…";
        var progress = new Progress<ReferenceUpdateProgress>(update =>
        {
            ReferenceStatus = update.Stage == ReferenceUpdateStage.Downloading
                ? $"Downloading reference data… {update.Bytes / 1048576.0:N1} MiB"
                : $"Building index… {update.Sheets:N0} sheets · {update.Lines:N0} lines";
        });
        try
        {
            var result = await _reference.UpdateAsync(SelectedGameLanguage.Code, progress, cancellationToken);
            ReferenceStatus = result.Detail;
            if (_reference.IsAvailable)
                ReferenceStatus += $" · {_reference.LineCount:N0} lines ({_reference.SourceLanguageCode} → ru)";
        }
        catch (OperationCanceledException)
        {
            RefreshReferenceStatus();
            ReferenceStatus = "Update cancelled. " + ReferenceStatus;
        }
        catch (Exception ex)
        {
            ReferenceStatus = $"Reference update failed: {ex.Message}";
        }
    }

    public void Dispose() => UpdateReferenceCommand.Cancel();
}
