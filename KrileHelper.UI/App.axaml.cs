using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;
using KrileHelper.UI.ViewModels;
using KrileHelper.UI.Views;

namespace KrileHelper.UI;

public partial class App : Application
{
    public SettingsService Settings { get; private set; } = null!;
    public ChatCodeRegistry Registry { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Settings = new SettingsService();
        Registry = ChatCodeRegistry.LoadDefault();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel(Settings, Registry);
            var window = new MainWindow { DataContext = vm };
            ApplyWindowSettings(window, Settings.Current.Window);
            window.Closing += (_, _) => CaptureWindowSettings(window);

            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) =>
            {
                CaptureWindowSettings(window);
                vm.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplyWindowSettings(Window window, WindowSettings ws)
    {
        window.Width = ws.Width;
        window.Height = ws.Height;
        if (ws.X is int x && ws.Y is int y)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(x, y);
        }
    }

    private void CaptureWindowSettings(Window window)
    {
        var ws = Settings.Current.Window;
        ws.Width = (int)window.Width;
        ws.Height = (int)window.Height;
        ws.X = window.Position.X;
        ws.Y = window.Position.Y;
        Settings.Save();
    }
}
