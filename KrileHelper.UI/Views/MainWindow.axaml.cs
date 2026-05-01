using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KrileHelper.UI.ViewModels;

namespace KrileHelper.UI.Views;

public partial class MainWindow : Window
{
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.LinesAppended += (_, _) => ChatScroll.ScrollToEnd();
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

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
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
