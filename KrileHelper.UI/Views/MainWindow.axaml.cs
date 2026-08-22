using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KrileHelper.UI.ViewModels;

namespace KrileHelper.UI.Views;

public partial class MainWindow : Window
{
    private SettingsWindow? _settingsWindow;
    private bool _wasAtBottom = true;

    public event EventHandler? HideRequested;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        vm.LinesAppended += (_, _) => { if (_wasAtBottom) ChatScroll.ScrollToEnd(); };
        ChatScroll.PropertyChanged += OnChatScrollPropertyChanged;
    }

    // The layout pass runs after lines are added or a translation lands on a
    // line, so ScrollToEnd() at append time targets a stale extent. Re-pin on
    // extent growth: if the user was at the bottom, follow the new content
    // down; if they scrolled up to read, leave them alone.
    private void OnChatScrollPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
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

        _settingsWindow = new SettingsWindow
        {
            DataContext = new SettingsWindowViewModel(vm.Settings, vm.Registry),
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }
}
