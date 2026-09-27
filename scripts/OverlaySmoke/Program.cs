using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using KrileHelper.UI.Services;
using KrileHelper.UI.Views;
using Sharlayan.Core.Dialogue;

if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
{
    Console.Error.WriteLine("Requires a Linux X11/XWayland desktop with a window manager; headless drawing cannot verify native stacking.");
    return 1;
}
return AppBuilder.Configure<OverlaySmokeApplication>().UsePlatformDetect().WithInterFont()
    .StartWithClassicDesktopLifetime(args);

public sealed class OverlaySmokeApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            Window? background = null;
            DialogueOverlayWindow? overlay = null;
            var result = 0;
            try
            {
                using var inspection = new X11Inspection();
                if (inspection.Property(inspection.Root, "_NET_SUPPORTING_WM_CHECK").Length == 0)
                    throw new InvalidOperationException("A running EWMH window manager is required, not just an X server.");
                using var native = new LinuxGameWindow();
                using var assets = new GameAssets();
                background = new Window
                {
                    Title = "Krile native overlay regression", Width = 440, Height = 180,
                    ShowActivated = false, ShowInTaskbar = false, Background = Brushes.DarkSlateGray,
                    WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(1000, 500),
                };
                background.Show();
                overlay = new DialogueOverlayWindow(assets, native);
                var game = new GameWindowState(background.Position, new PixelSize(660, 270), false, null);
                var backgroundId = Xid(background);
                var aboveAtom = inspection.Atom("_NET_WM_STATE_ABOVE");
                for (var cycle = 1; cycle <= 10; cycle++)
                {
                    if (!overlay.Present(game, AddonBounds.From(0, 0, 600, 180, 1), DialogueSurface.Window,
                        "Native stacking check", "The same overlay must remain above this window after reopening.", null))
                        throw new InvalidOperationException("Overlay refused presentation: " + native.Status);
                    var overlayId = Xid(overlay);
                    // WM messages are asynchronous. Wait for observable native state, not merely
                    // Avalonia's cached Topmost/IsVisible values (both stayed true during the bug).
                    var deadline = Environment.TickCount64 + 2000;
                    bool above, stacked;
                    do
                    {
                        await Task.Delay(25);
                        above = inspection.Property(overlayId, "_NET_WM_STATE").Contains(aboveAtom);
                        var order = inspection.Property(inspection.Root, "_NET_CLIENT_LIST_STACKING");
                        var backgroundIndex = Array.IndexOf(order, backgroundId);
                        stacked = backgroundIndex >= 0 && Array.IndexOf(order, overlayId) > backgroundIndex;
                    } while ((!above || !stacked) && Environment.TickCount64 < deadline);
                    var inputs = inspection.InputRectangleCount(overlayId);
                    var active = inspection.Property(inspection.Root, "_NET_ACTIVE_WINDOW").FirstOrDefault();
                    Console.WriteLine($"Show {cycle}: nativeAbove={above}, stackedAboveBackground={stacked}, inputRectangles={inputs}, stoleFocus={active == overlayId}");
                    if (!above || !stacked || inputs != 0 || active == overlayId)
                        throw new InvalidOperationException($"Show {cycle} lost native stacking, click-through, or non-activation.");
                    overlay.Hide();
                    // Wait until the WM has processed withdrawal before testing the next map.
                    deadline = Environment.TickCount64 + 2000;
                    while (inspection.Property(inspection.Root, "_NET_CLIENT_LIST").Contains(overlayId))
                    {
                        if (Environment.TickCount64 >= deadline) throw new InvalidOperationException("Overlay did not withdraw after Hide().");
                        await Task.Delay(25);
                    }
                }
                Console.WriteLine("PASS: native stacking survives repeated hide/show with identical text.");
            }
            catch (Exception ex) { result = 1; Console.Error.WriteLine("FAIL: " + ex.Message); }
            finally
            {
                overlay?.Close();
                background?.Close();
                desktop.Shutdown(result);
            }
        });
        base.OnFrameworkInitializationCompleted();
    }

    private static uint Xid(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        return handle?.HandleDescriptor == "XID" ? (uint)handle.Handle
            : throw new InvalidOperationException("The smoke test requires real X11 windows.");
    }
}

// Inspect the compositor independently of Avalonia and the application's native-window wrapper.
internal sealed class X11Inspection : IDisposable
{
    private readonly nint _connection;
    private readonly Dictionary<string, uint> _atoms = new(StringComparer.Ordinal);
    public uint Root { get; }

    public X11Inspection()
    {
        _connection = Native.xcb_connect(null, out var screen);
        if (_connection == 0 || Native.xcb_connection_has_error(_connection) != 0)
        {
            Dispose();
            throw new InvalidOperationException("Cannot connect to X11.");
        }
        var iterator = Native.xcb_setup_roots_iterator(Native.xcb_get_setup(_connection));
        for (var i = 0; i < screen && iterator.Remaining > 0; i++) Native.xcb_screen_next(ref iterator);
        if (iterator.Remaining == 0) { Dispose(); throw new InvalidOperationException("No X11 screen."); }
        Root = (uint)Marshal.ReadInt32(iterator.Data);
    }

    public uint Atom(string name)
    {
        if (_atoms.TryGetValue(name, out var atom)) return atom;
        var reply = Native.xcb_intern_atom_reply(_connection,
            Native.xcb_intern_atom(_connection, 0, (ushort)name.Length, name), out var error);
        Native.free(error);
        if (reply == 0) throw new InvalidOperationException("Cannot resolve X11 atom: " + name);
        try { atom = (uint)Marshal.ReadInt32(reply, 8); _atoms.Add(name, atom); return atom; }
        finally { Native.free(reply); }
    }

    public uint[] Property(uint window, string name)
    {
        var reply = Native.xcb_get_property_reply(_connection,
            Native.xcb_get_property(_connection, 0, window, Atom(name), 0, 0, 4096), out var error);
        Native.free(error);
        if (reply == 0) return [];
        try
        {
            if (Marshal.ReadByte(reply, 1) != 32) return [];
            var result = new uint[Marshal.ReadInt32(reply, 16)];
            var values = Native.xcb_get_property_value(reply);
            for (var i = 0; i < result.Length; i++) result[i] = (uint)Marshal.ReadInt32(values, i * 4);
            return result;
        }
        finally { Native.free(reply); }
    }

    public int InputRectangleCount(uint window)
    {
        var reply = Native.xcb_shape_get_rectangles_reply(_connection,
            Native.xcb_shape_get_rectangles(_connection, window, 2), out var error);
        Native.free(error);
        if (reply == 0) throw new InvalidOperationException("Cannot inspect the native input region.");
        try { return Marshal.ReadInt32(reply, 8); }
        finally { Native.free(reply); }
    }

    public void Dispose() { if (_connection != 0) Native.xcb_disconnect(_connection); }

    private static class Native
    {
        private const string Xcb = "libxcb.so.1";
        [StructLayout(LayoutKind.Sequential)] public struct Iterator { public nint Data; public int Remaining; public int Index; }
        [StructLayout(LayoutKind.Sequential)] public struct Cookie { public uint Sequence; }
        [DllImport(Xcb)] public static extern nint xcb_connect(string? display, out int screen);
        [DllImport(Xcb)] public static extern int xcb_connection_has_error(nint connection);
        [DllImport(Xcb)] public static extern void xcb_disconnect(nint connection);
        [DllImport(Xcb)] public static extern nint xcb_get_setup(nint connection);
        [DllImport(Xcb)] public static extern Iterator xcb_setup_roots_iterator(nint setup);
        [DllImport(Xcb)] public static extern void xcb_screen_next(ref Iterator iterator);
        [DllImport(Xcb)] public static extern Cookie xcb_intern_atom(nint connection, byte onlyExisting, ushort length, string name);
        [DllImport(Xcb)] public static extern nint xcb_intern_atom_reply(nint connection, Cookie cookie, out nint error);
        [DllImport(Xcb)] public static extern Cookie xcb_get_property(nint connection, byte delete, uint window, uint property, uint type, uint offset, uint length);
        [DllImport(Xcb)] public static extern nint xcb_get_property_reply(nint connection, Cookie cookie, out nint error);
        [DllImport(Xcb)] public static extern nint xcb_get_property_value(nint reply);
        [DllImport("libxcb-shape.so.0")] public static extern Cookie xcb_shape_get_rectangles(nint connection, uint window, byte kind);
        [DllImport("libxcb-shape.so.0")] public static extern nint xcb_shape_get_rectangles_reply(nint connection, Cookie cookie, out nint error);
        [DllImport("libc")] public static extern void free(nint pointer);
    }
}
