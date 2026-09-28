using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;

namespace KrileHelper.UI.ViewModels;

/// <summary>
/// A named overlay tab and its live view of the retained chat history.
/// </summary>
public sealed partial class ChatTabViewModel : ObservableObject
{
    private readonly ObservableCollection<ChatLineDisplay> _lines = new();
    private readonly ReadOnlyObservableCollection<ChatLineDisplay> _readOnlyLines;
    private HashSet<string> _channels;

    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isEmpty = true;

    public bool IncludeAllChannels { get; private set; }
    public IReadOnlySet<string> Channels => _channels;
    public ReadOnlyObservableCollection<ChatLineDisplay> Lines => _readOnlyLines;

    internal ChatTabViewModel(ChatTabSettings settings)
    {
        Id = settings.Id;
        _name = settings.Name;
        IncludeAllChannels = settings.IncludeAllChannels;
        _channels = new HashSet<string>(settings.Channels ?? [], StringComparer.OrdinalIgnoreCase);
        _readOnlyLines = new ReadOnlyObservableCollection<ChatLineDisplay>(_lines);
    }

    internal bool UpdateDefinition(ChatTabSettings settings, IReadOnlyList<ChatLineDisplay> history)
    {
        if (!string.Equals(Name, settings.Name, StringComparison.Ordinal))
            Name = settings.Name;

        var includeAllChanged = IncludeAllChannels != settings.IncludeAllChannels;
        var configuredChannels = settings.Channels ?? [];
        var channelsChanged = !_channels.SetEquals(configuredChannels);
        if (!includeAllChanged && !channelsChanged)
            return false;

        IncludeAllChannels = settings.IncludeAllChannels;
        _channels = channelsChanged
            ? new HashSet<string>(configuredChannels, StringComparer.OrdinalIgnoreCase)
            : _channels;
        Rebuild(history);
        return true;
    }

    internal bool Matches(ChatLineDisplay display) =>
        IncludeAllChannels || _channels.Contains(display.Code);

    internal void Add(ChatLineDisplay display)
    {
        _lines.Add(display);
        IsEmpty = false;
    }

    internal void Remove(ChatLineDisplay display)
    {
        // Every display is appended at most once to a tab, and retained rows
        // preserve history order. The evicted item is therefore first or absent.
        if (_lines.Count == 0 || !ReferenceEquals(_lines[0], display)) return;
        _lines.RemoveAt(0);
        IsEmpty = _lines.Count == 0;
    }

    private void Rebuild(IReadOnlyList<ChatLineDisplay> history)
    {
        _lines.Clear();
        foreach (var display in history)
            if (Matches(display)) _lines.Add(display);
        IsEmpty = _lines.Count == 0;
    }
}

/// <summary>
/// Owns the bounded chat history and the filtered collections displayed by each tab.
/// </summary>
public sealed class ChatTabsViewModel : ObservableObject, IDisposable
{
    public const int MaxRetainedLines = 500;

    private readonly SettingsService _settings;
    private readonly ObservableCollection<ChatTabViewModel> _tabs = new();
    private readonly ReadOnlyObservableCollection<ChatTabViewModel> _readOnlyTabs;
    private readonly List<ChatLineDisplay> _history = new();
    private readonly EventHandler _settingsChangedHandler;
    private ChatTabViewModel? _selectedTab;
    private bool _disposed;
    private bool _initializing;

    public ReadOnlyObservableCollection<ChatTabViewModel> Tabs => _readOnlyTabs;
    public IReadOnlyList<ChatLineDisplay> History => _history;

    public ChatTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value)) return;
            if (value is not null && !_tabs.Contains(value))
                throw new ArgumentException("The selected tab is not part of this view model.", nameof(value));
            OnPropertyChanging();
            _selectedTab = value;
            OnPropertyChanged();
            if (!_initializing && value is not null)
            {
                _settings.Current.SelectedChatTabId = value.Id;
                // Selection is deliberately persisted without Changed: changing tabs
                // must not reset the translation generation or recreate translators.
                _settings.Save();
            }
        }
    }

    public ChatTabsViewModel(SettingsService settings)
    {
        _settings = settings;
        _readOnlyTabs = new ReadOnlyObservableCollection<ChatTabViewModel>(_tabs);
        _settingsChangedHandler = (_, _) => ReconcileSettings();
        _settings.Changed += _settingsChangedHandler;

        _initializing = true;
        try
        {
            EnsureValidSettings();
            ReconcileSettings();
        }
        finally
        {
            _initializing = false;
        }
    }

    /// <summary>Adds one already-created display to history and matching tabs.</summary>
    public void Add(ChatLineDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (_disposed) return;

        _history.Add(display);
        foreach (var tab in _tabs)
            if (tab.Matches(display)) tab.Add(display);

        if (_history.Count <= MaxRetainedLines) return;
        var oldest = _history[0];
        _history.RemoveAt(0);
        foreach (var tab in _tabs)
            tab.Remove(oldest);
    }

    private void EnsureValidSettings()
    {
        var configured = _settings.Current.ChatTabs;
        if (configured is null || configured.Count == 0)
        {
            _settings.Current.ChatTabs = [new ChatTabSettings
            {
                Id = "all",
                Name = "All",
                IncludeAllChannels = true,
            }];
            _settings.Current.SelectedChatTabId = "all";
            _settings.Save();
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;
        for (var index = 0; index < configured.Count; index++)
        {
            var tab = configured[index];
            if (tab is null)
            {
                tab = new ChatTabSettings();
                configured[index] = tab;
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(tab.Id) || !seen.Add(tab.Id))
            {
                tab.Id = Guid.NewGuid().ToString("N");
                changed = true;
                seen.Add(tab.Id);
            }
            tab.Name ??= "New tab";
            tab.Channels ??= new List<string>();
        }

        if (changed) _settings.Save();
    }

    private void ReconcileSettings()
    {
        if (_disposed) return;
        EnsureValidSettings();
        var configured = _settings.Current.ChatTabs;
        if (configured is null || configured.Count == 0) return;

        var selectedId = _settings.Current.SelectedChatTabId;
        var desired = new List<ChatTabViewModel>(configured.Count);
        foreach (var definition in configured)
        {
            var existing = _tabs.FirstOrDefault(tab => tab.Id == definition.Id);
            if (existing is null)
            {
                existing = new ChatTabViewModel(definition);
                foreach (var display in _history)
                    if (existing.Matches(display)) existing.Add(display);
            }
            else
            {
                existing.UpdateDefinition(definition, _history);
            }
            desired.Add(existing);
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var currentIndex = _tabs.IndexOf(desired[index]);
            if (currentIndex < 0)
                _tabs.Insert(index, desired[index]);
            else if (currentIndex != index)
                _tabs.Move(currentIndex, index);
        }

        while (_tabs.Count > desired.Count)
            _tabs.RemoveAt(_tabs.Count - 1);

        var selected = desired.FirstOrDefault(tab => tab.Id == selectedId) ?? desired[0];
        if (!string.Equals(_settings.Current.SelectedChatTabId, selected.Id, StringComparison.Ordinal))
        {
            _settings.Current.SelectedChatTabId = selected.Id;
            _settings.Save();
        }

        if (!ReferenceEquals(_selectedTab, selected))
        {
            OnPropertyChanging(nameof(SelectedTab));
            _selectedTab = selected;
            OnPropertyChanged(nameof(SelectedTab));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.Changed -= _settingsChangedHandler;
    }
}
