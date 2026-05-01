using Avalonia.Controls;
using Avalonia.Threading;
using KrileHelper.UI.Views;

namespace KrileHelper.UI.Services;

public sealed class OverlayWindowController
{
    private readonly MainWindow _window;
    private readonly Action _captureWindowSettings;

    public OverlayWindowController(MainWindow window, Action captureWindowSettings)
    {
        _window = window;
        _captureWindowSettings = captureWindowSettings;
    }

    public event EventHandler? VisibilityChanged;

    public Window Window => _window;
    public bool IsOverlayVisible => _window.IsVisible;

    public void ToggleOverlay() => Dispatcher.UIThread.Post(() =>
    {
        if (_window.IsVisible)
            HideOverlayCore();
        else
            ShowOverlayCore();
    });

    public void ShowOverlay() => Dispatcher.UIThread.Post(ShowOverlayCore);

    public void HideOverlay() => Dispatcher.UIThread.Post(HideOverlayCore);

    public void ShowSettings() => Dispatcher.UIThread.Post(() => _window.ShowSettingsWindow());

    private void ShowOverlayCore()
    {
        if (!_window.IsVisible)
            _window.Show();

        _window.Activate();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HideOverlayCore()
    {
        if (!_window.IsVisible)
            return;

        _captureWindowSettings();
        _window.Hide();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
