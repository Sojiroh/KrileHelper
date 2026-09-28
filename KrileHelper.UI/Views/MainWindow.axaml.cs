using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KrileHelper.UI.ViewModels;

namespace KrileHelper.UI.Views;

public partial class MainWindow : Window
{
    private SettingsWindow? _settingsWindow;
    private bool _wasAtBottom = true;
    private MainWindowViewModel? _viewModel;
    private readonly Dictionary<string, (Vector Offset, bool WasAtBottom)> _tabScroll = new();
    private bool _switchingTabs;
    private int _tabSwitchVersion;

    public event EventHandler? HideRequested;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ChatScroll.PropertyChanged += OnChatScrollPropertyChanged;
        Closed += (_, _) => DetachViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachViewModel();
        _tabScroll.Clear();
        _wasAtBottom = true;
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is null) return;
        _viewModel.ChatTabs.PropertyChanging += OnTabsPropertyChanging;
        _viewModel.ChatTabs.PropertyChanged += OnTabsPropertyChanged;
        ((INotifyCollectionChanged)_viewModel.ChatTabs.Tabs).CollectionChanged += OnTabsCollectionChanged;
        ScheduleScrollRestore();
    }

    private void DetachViewModel()
    {
        _tabSwitchVersion++;
        _switchingTabs = false;
        if (_viewModel is null) return;
        _viewModel.ChatTabs.PropertyChanging -= OnTabsPropertyChanging;
        _viewModel.ChatTabs.PropertyChanged -= OnTabsPropertyChanged;
        ((INotifyCollectionChanged)_viewModel.ChatTabs.Tabs).CollectionChanged -= OnTabsCollectionChanged;
        _viewModel = null;
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset) _tabScroll.Clear();
        if (e.Action is not (NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace) ||
            e.OldItems is null) return;
        foreach (ChatTabViewModel tab in e.OldItems) _tabScroll.Remove(tab.Id);
    }

    private void OnTabsPropertyChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (e.PropertyName != nameof(ChatTabsViewModel.SelectedTab)) return;
        if (!_switchingTabs && _viewModel?.ChatTabs.SelectedTab is { } tab &&
            _viewModel.ChatTabs.Tabs.Contains(tab))
            _tabScroll[tab.Id] = (ChatScroll.Offset, _wasAtBottom);
        // Replacing ItemsSource temporarily changes extent and offset. Those
        // layout changes must not overwrite the outgoing tab's reading state.
        _switchingTabs = true;
    }

    private void OnTabsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatTabsViewModel.SelectedTab))
            ScheduleScrollRestore();
    }

    private void ScheduleScrollRestore()
    {
        _switchingTabs = true;
        var version = ++_tabSwitchVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _tabSwitchVersion) return;
            ChatScroll.UpdateLayout();
            var tab = _viewModel?.ChatTabs.SelectedTab;
            var state = tab is not null && _tabScroll.TryGetValue(tab.Id, out var saved)
                ? saved : (Offset: default(Vector), WasAtBottom: true);
            _wasAtBottom = state.WasAtBottom;
            if (_wasAtBottom) ChatScroll.ScrollToEnd();
            else ChatScroll.Offset = state.Offset;
            ChatScroll.UpdateLayout();
            _switchingTabs = false;
        }, DispatcherPriority.Loaded);
    }

    // The layout pass runs after lines are added or a translation lands on a
    // line, so ScrollToEnd() at append time targets a stale extent. Re-pin on
    // extent growth: if the user was at the bottom, follow the new content
    // down; if they scrolled up to read, leave them alone.
    private void OnChatScrollPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_switchingTabs) return;
        if (e.Property == ScrollViewer.OffsetProperty)
        {
            var max = Math.Max(0, ChatScroll.Extent.Height - ChatScroll.Viewport.Height);
            _wasAtBottom = ChatScroll.Offset.Y >= max - 1;
        }
        else if (e.Property == ScrollViewer.ExtentProperty && _wasAtBottom)
        {
            ChatScroll.ScrollToEnd();
        }
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnResizeGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginResizeDrag(WindowEdge.SouthEast, e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => HideRequested?.Invoke(this, EventArgs.Empty);

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => ShowSettingsWindow();

    public void ShowSettingsWindow()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (_settingsWindow is { } w && w.IsVisible)
        {
            w.Activate();
            return;
        }

        var settingsViewModel = new SettingsWindowViewModel(vm.Settings, vm.Registry, vm.ReferenceTranslations);
        _settingsWindow = new SettingsWindow { DataContext = settingsViewModel };
        _settingsWindow.Closed += (_, _) =>
        {
            settingsViewModel.Dispose();
            _settingsWindow = null;
        };
        _settingsWindow.Show(this);
    }
}
