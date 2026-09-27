using System.Runtime.InteropServices;
using Avalonia;

namespace KrileHelper.UI.Services;

public readonly record struct GameWindowState(PixelPoint Origin, PixelSize Size, bool IsForeground, PixelPoint? Cursor);

/// <summary>
/// An independent XCB connection tracks the Proton window, including under XWayland.
/// Checked replies handle windows disappearing without installing a process-wide Xlib error handler.
/// Native Wayland windows are deliberately not guessed from their titles or screen dimensions.
/// </summary>
public sealed class LinuxGameWindow : IDisposable
{
    private nint _connection;
    private uint _root;
    private uint _clientsAtom;
    private uint _pidAtom;
    private uint _activeAtom;
    private uint _window;
    private int _pid;
    private long _nextDiscovery;
    public string Status { get; private set; } = "Aligned overlays require an X11/XWayland game window.";

    public LinuxGameWindow()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return;
        try
        {
            _connection = Native.xcb_connect(null, out var screenNumber);
            if (_connection == 0 || Native.xcb_connection_has_error(_connection) != 0)
            {
                Dispose();
                return;
            }
            var screens = Native.xcb_setup_roots_iterator(Native.xcb_get_setup(_connection));
            for (int i = 0; i < screenNumber && screens.Remaining > 0; i++) Native.xcb_screen_next(ref screens);
            if (screens.Remaining == 0) { Dispose(); return; }
            _root = unchecked((uint)Marshal.ReadInt32(screens.Data));
            _clientsAtom = Intern("_NET_CLIENT_LIST");
            _pidAtom = Intern("_NET_WM_PID");
            _activeAtom = Intern("_NET_ACTIVE_WINDOW");
        }
        catch (DllNotFoundException) { Dispose(); }
    }

    public bool TryGet(int pid, out GameWindowState state)
    {
        state = default;
        if (_connection == 0) return false;
        if (_pid != pid)
        {
            _pid = pid;
            _window = 0;
            _nextDiscovery = 0;
        }
        if (_window == 0 && Environment.TickCount64 >= _nextDiscovery)
        {
            _nextDiscovery = Environment.TickCount64 + 1000;
            _window = FindWindow(pid);
        }
        if (_window == 0)
        {
            Status = "No X11/XWayland window belongs to the attached game; aligned overlay is hidden.";
            return false;
        }
        var geometry = Native.xcb_get_geometry_reply(_connection, Native.xcb_get_geometry(_connection, _window), out var error);
        Native.free(error);
        if (geometry == 0) { _window = 0; return false; }
        int width, height;
        try
        {
            width = (ushort)Marshal.ReadInt16(geometry, 16);
            height = (ushort)Marshal.ReadInt16(geometry, 18);
        }
        finally { Native.free(geometry); }
        var position = Native.xcb_translate_coordinates_reply(_connection,
            Native.xcb_translate_coordinates(_connection, _window, _root, 0, 0), out error);
        Native.free(error);
        if (position == 0) { _window = 0; return false; }
        PixelPoint origin;
        try { origin = new PixelPoint(Marshal.ReadInt16(position, 12), Marshal.ReadInt16(position, 14)); }
        finally { Native.free(position); }
        var foreground = SingleProperty(_root, _activeAtom) == _window;
        PixelPoint? cursor = null;
        var pointer = Native.xcb_query_pointer_reply(_connection, Native.xcb_query_pointer(_connection, _root), out error);
        Native.free(error);
        if (pointer != 0)
        {
            if (Marshal.ReadByte(pointer, 1) != 0)
                cursor = new PixelPoint(Marshal.ReadInt16(pointer, 16), Marshal.ReadInt16(pointer, 18));
            Native.free(pointer);
        }
        state = new GameWindowState(origin, new PixelSize(width, height), foreground, cursor);
        Status = foreground ? "Following the game window via X11/XWayland." : "Game is not foreground; aligned overlay is hidden.";
        return width > 0 && height > 0;
    }

    public bool MakeClickThrough(nint window)
    {
        if (_connection == 0 || window == 0) return false;
        try
        {
            var cookie = Native.xcb_shape_rectangles_checked(_connection, 0, 2, 0,
                unchecked((uint)window), 0, 0, 0, 0);
            var error = Native.xcb_request_check(_connection, cookie);
            if (error != 0)
            {
                Native.free(error);
                Status = "X11 Shape input regions unavailable; aligned overlay is hidden to avoid intercepting game input.";
                return false;
            }
            Native.xcb_flush(_connection);
            return true;
        }
        catch (DllNotFoundException)
        {
            Status = "libxcb-shape is required for a click-through dialogue overlay.";
            return false;
        }
    }

    private uint FindWindow(int pid)
    {
        var reply = Property(_root, _clientsAtom);
        if (reply == 0) return 0;
        try
        {
            var length = Marshal.ReadInt32(reply, 16);
            var values = Native.xcb_get_property_value(reply);
            for (int i = 0; i < length; i++)
            {
                uint candidate = unchecked((uint)Marshal.ReadInt32(values, i * 4));
                if (SingleProperty(candidate, _pidAtom) == (uint)pid) return candidate;
            }
            return 0;
        }
        finally { Native.free(reply); }
    }

    private uint SingleProperty(uint window, uint atom)
    {
        var reply = Property(window, atom);
        if (reply == 0) return 0;
        try
        {
            return Marshal.ReadInt32(reply, 16) > 0
                ? unchecked((uint)Marshal.ReadInt32(Native.xcb_get_property_value(reply))) : 0;
        }
        finally { Native.free(reply); }
    }

    private nint Property(uint window, uint atom)
    {
        var reply = Native.xcb_get_property_reply(_connection,
            Native.xcb_get_property(_connection, 0, window, atom, 0, 0, 4096), out var error);
        Native.free(error);
        if (reply != 0 && Marshal.ReadByte(reply, 1) != 32)
        {
            Native.free(reply);
            return 0;
        }
        return reply;
    }

    private uint Intern(string name)
    {
        var reply = Native.xcb_intern_atom_reply(_connection,
            Native.xcb_intern_atom(_connection, 0, (ushort)name.Length, name), out var error);
        Native.free(error);
        if (reply == 0) return 0;
        try { return unchecked((uint)Marshal.ReadInt32(reply, 8)); }
        finally { Native.free(reply); }
    }

    public void Dispose()
    {
        if (_connection != 0) Native.xcb_disconnect(_connection);
        _connection = 0;
    }

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
        [DllImport(Xcb)] public static extern Cookie xcb_get_geometry(nint connection, uint drawable);
        [DllImport(Xcb)] public static extern nint xcb_get_geometry_reply(nint connection, Cookie cookie, out nint error);
        [DllImport(Xcb)] public static extern Cookie xcb_translate_coordinates(nint connection, uint source, uint destination, short x, short y);
        [DllImport(Xcb)] public static extern nint xcb_translate_coordinates_reply(nint connection, Cookie cookie, out nint error);
        [DllImport(Xcb)] public static extern Cookie xcb_query_pointer(nint connection, uint window);
        [DllImport(Xcb)] public static extern nint xcb_query_pointer_reply(nint connection, Cookie cookie, out nint error);
        [DllImport("libxcb-shape.so.0")] public static extern Cookie xcb_shape_rectangles_checked(nint connection, byte operation, byte kind, byte ordering, uint window, short x, short y, uint count, nint rectangles);
        [DllImport(Xcb)] public static extern nint xcb_request_check(nint connection, Cookie cookie);
        [DllImport(Xcb)] public static extern int xcb_flush(nint connection);
        [DllImport("libc")] public static extern void free(nint pointer);
    }
}
