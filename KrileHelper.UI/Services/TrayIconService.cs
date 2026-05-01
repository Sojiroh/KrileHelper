using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace KrileHelper.UI.Services;

public sealed class TrayIconService : IAsyncDisposable
{
    private readonly Application _application;
    private readonly Avalonia.Controls.TrayIcon _trayIcon;

    public TrayIconService(Application application, IClassicDesktopStyleApplicationLifetime desktop, OverlayWindowController overlay)
    {
        _application = application;

        var menu = new NativeMenu();

        var toggleItem = new NativeMenuItem("Show / Hide overlay");
        toggleItem.Click += (_, _) => overlay.ToggleOverlay();
        menu.Items.Add(toggleItem);

        var settingsItem = new NativeMenuItem("Settings...");
        settingsItem.Click += (_, _) => overlay.ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => desktop.Shutdown();
        menu.Items.Add(quitItem);

        _trayIcon = new Avalonia.Controls.TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = "Krile Helper",
            Menu = menu,
            IsVisible = true,
        };
        _trayIcon.Clicked += (_, _) => overlay.ToggleOverlay();

        var icons = Avalonia.Controls.TrayIcon.GetIcons(application) ?? new TrayIcons();
        icons.Add(_trayIcon);
        Avalonia.Controls.TrayIcon.SetIcons(application, icons);
    }

    private static WindowIcon LoadIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://KrileHelper.UI/Assets/icon.png"));
        return new WindowIcon(stream);
    }

    public ValueTask DisposeAsync()
    {
        _trayIcon.IsVisible = false;
        var icons = Avalonia.Controls.TrayIcon.GetIcons(_application);
        icons?.Remove(_trayIcon);
        _trayIcon.Dispose();
        return ValueTask.CompletedTask;
    }
}
