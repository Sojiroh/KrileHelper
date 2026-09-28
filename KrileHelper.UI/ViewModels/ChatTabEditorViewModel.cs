using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;

namespace KrileHelper.UI.ViewModels;

/// <summary>
/// Edits the configured overlay tabs without changing the global channel privacy
/// settings (Show and Translate).
/// </summary>
public sealed partial class ChatTabEditorViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ChatCodeRegistry _registry;
    private bool _refreshingChannels;
    private ChatTabEditorTabViewModel? _selectedTab;

    public ObservableCollection<ChatTabEditorTabViewModel> Tabs { get; } = new();
    public ObservableCollection<ChatTabEditorChannelViewModel> Channels { get; } = new();

    public ChatTabEditorTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetProperty(ref _selectedTab, value)) return;
            RefreshChannels();
            OnPropertyChanged(nameof(CanDeleteTab));
            OnPropertyChanged(nameof(HasEmptyFilter));
        }
    }

    public bool CanDeleteTab => Tabs.Count > 1;
    public bool HasEmptyFilter => SelectedTab is { IncludeAllChannels: false, SelectedChannelCount: 0 };

    public ChatTabEditorViewModel(SettingsService settings, ChatCodeRegistry registry)
    {
        _settings = settings;
        _registry = registry;

        var configured = settings.Current.ChatTabs ?? new List<ChatTabSettings>();
        if (configured.Count == 0)
            configured.Add(new ChatTabSettings { Id = "all", Name = "All", IncludeAllChannels = true });
        settings.Current.ChatTabs = configured;

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in configured)
        {
            if (string.IsNullOrWhiteSpace(model.Id) || !ids.Add(model.Id))
            {
                model.Id = Guid.NewGuid().ToString("N");
                ids.Add(model.Id);
            }
            model.Channels = CanonicalChannels(model.Channels);
            Tabs.Add(new ChatTabEditorTabViewModel(this, model));
        }

        SelectedTab = Tabs.FirstOrDefault(tab => tab.Id == settings.Current.SelectedChatTabId)
            ?? Tabs[0];
    }

    [RelayCommand]
    private void AddTab()
    {
        var model = new ChatTabSettings { Name = "New tab" };
        _settings.Current.ChatTabs.Add(model);
        var tab = new ChatTabEditorTabViewModel(this, model);
        Tabs.Add(tab);
        OnPropertyChanged(nameof(CanDeleteTab));
        DeleteTabCommand.NotifyCanExecuteChanged();
        SelectedTab = tab;
        Persist();
    }
    private bool CanDeleteTabCommand() => CanDeleteTab;

    [RelayCommand(CanExecute = nameof(CanDeleteTabCommand))]
    private void DeleteTab()
    {
        if (SelectedTab is not { } selected || Tabs.Count <= 1) return;

        var index = Tabs.IndexOf(selected);
        _settings.Current.ChatTabs.Remove(selected.Model);
        Tabs.RemoveAt(index);

        // Select a valid fallback before notifying subscribers or saving.
        var fallback = Tabs[Math.Min(index, Tabs.Count - 1)];
        SelectedTab = fallback;
        if (string.Equals(_settings.Current.SelectedChatTabId, selected.Id, StringComparison.OrdinalIgnoreCase))
            _settings.Current.SelectedChatTabId = fallback.Id;
        OnPropertyChanged(nameof(CanDeleteTab));
        DeleteTabCommand.NotifyCanExecuteChanged();
        Persist();
    }

    internal void UpdateName(ChatTabEditorTabViewModel tab, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var normalized = value.Trim();
        if (tab.Model.Name == normalized) return;
        tab.Model.Name = normalized;
        Persist();
    }

    internal void UpdateIncludeAll(ChatTabEditorTabViewModel tab, bool value)
    {
        tab.Model.IncludeAllChannels = value;
        OnPropertyChanged(nameof(HasEmptyFilter));
        Persist();
    }

    internal void UpdateChannel(ChatTabEditorChannelViewModel channel, bool value)
    {
        if (_refreshingChannels || SelectedTab is not { } tab) return;

        if (value)
        {
            if (!tab.Model.Channels.Contains(channel.Code, StringComparer.OrdinalIgnoreCase))
                tab.Model.Channels.Add(channel.Code);
        }
        else
        {
            tab.Model.Channels.RemoveAll(code => string.Equals(code, channel.Code, StringComparison.OrdinalIgnoreCase));
        }
        tab.OnSelectionChanged();
        OnPropertyChanged(nameof(HasEmptyFilter));
        Persist();
    }

    private void RefreshChannels()
    {
        _refreshingChannels = true;
        try
        {
            Channels.Clear();
            foreach (var info in _registry.ByCode.Values.OrderBy(info => info.Code))
            {
                Channels.Add(new ChatTabEditorChannelViewModel(this, info,
                    SelectedTab?.Model.Channels.Contains(info.Code, StringComparer.OrdinalIgnoreCase) == true));
            }
        }
        finally
        {
            _refreshingChannels = false;
        }
        OnPropertyChanged(nameof(HasEmptyFilter));
    }

    private List<string> CanonicalChannels(IEnumerable<string>? channels)
    {
        if (channels is null) return new List<string>();
        var canonical = new List<string>();
        foreach (var code in channels)
        {
            var info = _registry.Lookup(code);
            if (info is not null && !canonical.Contains(info.Code, StringComparer.OrdinalIgnoreCase))
                canonical.Add(info.Code);
        }
        return canonical;
    }


    private void Persist() => _settings.NotifyChanged();

}

public sealed class ChatTabEditorTabViewModel : ObservableObject
{
    private readonly ChatTabEditorViewModel _owner;
    private string _name;
    private bool _includeAllChannels;

    internal ChatTabSettings Model { get; }
    public string Id => Model.Id;
    public int SelectedChannelCount => Model.Channels.Count;
    public bool IsCustomFilterEmpty => !IncludeAllChannels && SelectedChannelCount == 0;
    public bool IsCustomFilter => !IncludeAllChannels;

    public string Name
    {
        get => _name;
        set
        {
            var raw = value ?? "";
            if (!SetProperty(ref _name, raw)) return;
            _owner.UpdateName(this, raw);
        }
    }

    public bool IncludeAllChannels
    {
        get => _includeAllChannels;
        set
        {
            if (!SetProperty(ref _includeAllChannels, value)) return;
            OnPropertyChanged(nameof(IsCustomFilter));
            _owner.UpdateIncludeAll(this, value);
        }

    }

    internal ChatTabEditorTabViewModel(ChatTabEditorViewModel owner, ChatTabSettings model)
    {
        _owner = owner;
        Model = model;
        _name = string.IsNullOrWhiteSpace(model.Name) ? "New tab" : model.Name.Trim();
        model.Name = _name;
        _includeAllChannels = model.IncludeAllChannels;
    }

    internal void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedChannelCount));
        OnPropertyChanged(nameof(IsCustomFilterEmpty));
    }
}

public sealed class ChatTabEditorChannelViewModel : ObservableObject
{
    private readonly ChatTabEditorViewModel _owner;
    private bool _isSelected;

    public string Code { get; }
    public string Name { get; }
    public IBrush Color { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            _owner.UpdateChannel(this, value);
        }
    }

    internal ChatTabEditorChannelViewModel(ChatTabEditorViewModel owner, ChatCodeInfo info, bool isSelected)
    {
        _owner = owner;
        Code = info.Code;
        Name = info.Name;
        Color = info.Color;
        _isSelected = isSelected;
    }
}
