using System.Runtime.InteropServices;
using KrileHelper.UI.Models;
using Tmds.DBus.Protocol;

namespace KrileHelper.UI.Services;

public sealed class GlobalHotkeyService : IAsyncDisposable
{
    private readonly HotkeySettings _settings;
    private readonly OverlayWindowController _overlay;
    private readonly CancellationTokenSource _cts = new();
    private IAsyncDisposable? _backend;
    private Task? _startupTask;

    public GlobalHotkeyService(HotkeySettings settings, OverlayWindowController overlay)
    {
        _settings = settings;
        _overlay = overlay;
    }

    public Task StartAsync()
    {
        _startupTask = StartCoreAsync(_cts.Token);
        return _startupTask;
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_settings.ToggleOverlayEnabled)
                return;

            var shortcut = Shortcut.Parse(_settings.ToggleOverlayShortcut);
            var isWayland = string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            var hasX11 = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));

            if (isWayland)
            {
                PortalGlobalHotkeyBackend? portal = null;
                try
                {
                    portal = new PortalGlobalHotkeyBackend(shortcut, _overlay.ToggleOverlay);
                    await portal.StartAsync(cancellationToken).ConfigureAwait(false);
                    _backend = portal;
                    return;
                }
                catch (OperationCanceledException)
                {
                    if (portal is not null)
                        await portal.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (portal is not null)
                        await portal.DisposeAsync().ConfigureAwait(false);
                    Console.Error.WriteLine($"Wayland global shortcuts unavailable: {ex.Message}");
                }
            }

            if (!hasX11)
            {
                Console.Error.WriteLine("Global shortcuts unavailable: neither Wayland portal nor X11 DISPLAY is available.");
                return;
            }

            X11GlobalHotkeyBackend? x11 = null;
            try
            {
                x11 = new X11GlobalHotkeyBackend(shortcut, _overlay.ToggleOverlay);
                x11.Start();
                _backend = x11;
            }
            catch (Exception ex)
            {
                if (x11 is not null)
                    await x11.DisposeAsync().ConfigureAwait(false);
                Console.Error.WriteLine($"X11 global shortcut unavailable: {ex.Message}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Global shortcut startup failed: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_startupTask is not null)
        {
            try { await _startupTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        if (_backend is not null)
            await _backend.DisposeAsync().ConfigureAwait(false);

        _cts.Dispose();
    }

    private sealed record Shortcut(string DisplayText, string X11KeySym, uint X11Modifiers, string PortalTrigger)
    {
        public static Shortcut Parse(string value)
        {
            var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                throw new InvalidOperationException("Hotkey is empty.");

            uint x11Modifiers = 0;
            var portalModifiers = new List<string>();
            string? key = null;

            foreach (var part in parts)
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        x11Modifiers |= X11.ControlMask;
                        portalModifiers.Add("CTRL");
                        break;
                    case "alt":
                        x11Modifiers |= X11.Mod1Mask;
                        portalModifiers.Add("ALT");
                        break;
                    case "shift":
                        x11Modifiers |= X11.ShiftMask;
                        portalModifiers.Add("SHIFT");
                        break;
                    case "super":
                    case "meta":
                    case "win":
                    case "windows":
                        x11Modifiers |= X11.Mod4Mask;
                        portalModifiers.Add("LOGO");
                        break;
                    default:
                        key = part;
                        break;
                }
            }

            if (key is null)
                throw new InvalidOperationException($"Hotkey '{value}' does not contain a key.");

            var keySym = ToX11KeySym(key);
            var portalKey = ToPortalKey(key);
            portalModifiers.Add(portalKey);
            return new Shortcut(value, keySym, x11Modifiers, string.Join('+', portalModifiers));
        }

        private static string ToX11KeySym(string key) => key.ToLowerInvariant() switch
        {
            "space" => "space",
            "enter" => "Return",
            "return" => "Return",
            "esc" => "Escape",
            "escape" => "Escape",
            "tab" => "Tab",
            _ when key.Length == 1 => key.ToLowerInvariant(),
            _ => key,
        };

        private static string ToPortalKey(string key) => key.ToLowerInvariant() switch
        {
            "space" => "space",
            "enter" => "Return",
            "return" => "Return",
            "esc" => "Escape",
            "escape" => "Escape",
            "tab" => "Tab",
            _ when key.Length == 1 => key.ToLowerInvariant(),
            _ => key,
        };
    }

    private sealed class X11GlobalHotkeyBackend : IAsyncDisposable
    {
        private readonly Shortcut _shortcut;
        private readonly Action _onPressed;
        private readonly IntPtr _display;
        private readonly IntPtr _root;
        private readonly int _keyCode;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loopTask;

        public X11GlobalHotkeyBackend(Shortcut shortcut, Action onPressed)
        {
            _shortcut = shortcut;
            _onPressed = onPressed;

            _display = X11.XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
                throw new InvalidOperationException("Unable to open X11 display.");

            try
            {
                _root = X11.XDefaultRootWindow(_display);
                var keySym = X11.XStringToKeysym(_shortcut.X11KeySym);
                if (keySym == IntPtr.Zero)
                    throw new InvalidOperationException($"Unknown X11 keysym '{_shortcut.X11KeySym}'.");

                _keyCode = X11.XKeysymToKeycode(_display, keySym);
                if (_keyCode == 0)
                    throw new InvalidOperationException($"Unable to resolve keycode for '{_shortcut.X11KeySym}'.");
            }
            catch
            {
                X11.XCloseDisplay(_display);
                throw;
            }
        }

        public void Start()
        {
            var xError = X11.TrapErrors(_display, () =>
            {
                foreach (var modifiers in ModifierVariants(_shortcut.X11Modifiers))
                {
                    X11.XGrabKey(
                        _display,
                        _keyCode,
                        modifiers,
                        _root,
                        ownerEvents: false,
                        pointerMode: X11.GrabModeAsync,
                        keyboardMode: X11.GrabModeAsync);
                }

                X11.XSelectInput(_display, _root, X11.KeyPressMask);
            });

            if (xError != 0)
                throw new InvalidOperationException($"XGrabKey failed with X11 error {xError}. The shortcut may already be reserved by the desktop.");

            _loopTask = Task.Run(EventLoop);
        }

        private void EventLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                int pending;
                lock (X11.SyncRoot)
                    pending = X11.XPending(_display);

                if (pending == 0)
                {
                    Thread.Sleep(50);
                    continue;
                }

                XEvent ev;
                lock (X11.SyncRoot)
                    X11.XNextEvent(_display, out ev);

                if (ev.type == X11.KeyPress && ev.KeyEvent.keycode == _keyCode)
                    _onPressed();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();

            if (_loopTask is not null)
            {
                try { await _loopTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
                catch (TimeoutException) { }
            }

            lock (X11.SyncRoot)
            {
                foreach (var modifiers in ModifierVariants(_shortcut.X11Modifiers))
                    X11.XUngrabKey(_display, _keyCode, modifiers, _root);

                X11.XFlush(_display);
                X11.XCloseDisplay(_display);
            }
            _cts.Dispose();
        }

        private static IEnumerable<uint> ModifierVariants(uint modifiers)
        {
            yield return modifiers;
            yield return modifiers | X11.LockMask;
            yield return modifiers | X11.Mod2Mask;
            yield return modifiers | X11.LockMask | X11.Mod2Mask;
        }
    }

    private sealed class PortalGlobalHotkeyBackend : IAsyncDisposable
    {
        private const string Destination = "org.freedesktop.portal.Desktop";
        private const string DesktopPath = "/org/freedesktop/portal/desktop";
        private const string ShortcutId = "toggle-overlay";

        private readonly Shortcut _shortcut;
        private readonly Action _onPressed;
        private DBusConnection? _connection;
        private IDisposable? _activatedWatcher;
        private ObjectPath? _sessionHandle;

        public PortalGlobalHotkeyBackend(Shortcut shortcut, Action onPressed)
        {
            _shortcut = shortcut;
            _onPressed = onPressed;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var address = DBusAddress.Session ?? throw new InvalidOperationException("D-Bus session bus is not available.");
            _connection = new DBusConnection(address);
            await _connection.ConnectAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var desktop = new PortalDesktopService(_connection, Destination);
            var shortcuts = desktop.CreateGlobalShortcuts(DesktopPath);

            var version = await shortcuts.GetVersionAsync().ConfigureAwait(false);
            if (version < 2)
                throw new InvalidOperationException($"GlobalShortcuts portal version {version} is too old.");

            _activatedWatcher = await shortcuts.WatchActivatedAsync((ex, signal) =>
            {
                if (ex is not null)
                {
                    Console.Error.WriteLine($"Global shortcut portal signal failed: {ex.Message}");
                    return;
                }

                if (_sessionHandle is ObjectPath session && signal.SessionHandle == session && signal.ShortcutId == ShortcutId)
                    _onPressed();
            }, emitOnCapturedContext: false).ConfigureAwait(false);

            var createRequest = await shortcuts.CreateSessionAsync(new Dictionary<string, VariantValue>
            {
                ["handle_token"] = VariantValue.String(CreateToken("create")),
                ["session_handle_token"] = VariantValue.String(CreateToken("session")),
            }).ConfigureAwait(false);

            var createResponse = await WaitForResponseAsync(desktop, createRequest, cancellationToken).ConfigureAwait(false);
            _sessionHandle = ReadSessionHandle(createResponse.Results);

            var bindRequest = await shortcuts.BindShortcutsAsync(
                _sessionHandle.Value,
                new[]
                {
                    (ShortcutId, new Dictionary<string, VariantValue>
                    {
                        ["description"] = VariantValue.String("Show or hide the Krile Helper overlay"),
                        ["preferred_trigger"] = VariantValue.String(_shortcut.PortalTrigger),
                    }),
                },
                string.Empty,
                new Dictionary<string, VariantValue>
                {
                    ["handle_token"] = VariantValue.String(CreateToken("bind")),
                }).ConfigureAwait(false);

            await WaitForResponseAsync(desktop, bindRequest, cancellationToken).ConfigureAwait(false);
        }

        private static string CreateToken(string prefix) => $"krile_{prefix}_{Guid.NewGuid():N}";

        private static ObjectPath ReadSessionHandle(Dictionary<string, VariantValue> results)
        {
            if (!results.TryGetValue("session_handle", out var value))
                throw new InvalidOperationException("GlobalShortcuts portal did not return a session handle.");

            try
            {
                return value.GetObjectPath();
            }
            catch
            {
                return new ObjectPath(value.GetString());
            }
        }

        private static async Task<(uint Response, Dictionary<string, VariantValue> Results)> WaitForResponseAsync(PortalDesktopService desktop, ObjectPath requestPath, CancellationToken cancellationToken)
        {
            var request = desktop.CreateRequest(requestPath);
            var tcs = new TaskCompletionSource<(uint Response, Dictionary<string, VariantValue> Results)>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var watcher = await request.WatchResponseAsync((ex, response) =>
            {
                if (ex is not null)
                    tcs.TrySetException(ex);
                else
                    tcs.TrySetResult(response);
            }, emitOnCapturedContext: false).ConfigureAwait(false);

            var result = await tcs.Task.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            if (result.Response != 0)
                throw new InvalidOperationException($"GlobalShortcuts portal request failed or was cancelled (response {result.Response}).");

            return result;
        }

        public ValueTask DisposeAsync()
        {
            _activatedWatcher?.Dispose();
            _connection?.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static class X11
    {
        private const string LibX11 = "libX11.so.6";
        private static readonly XErrorHandler ErrorHandler = CaptureError;
        private static readonly IntPtr ErrorHandlerPtr = Marshal.GetFunctionPointerForDelegate(ErrorHandler);
        private static int _lastError;

        public static object SyncRoot { get; } = new();

        public const int KeyPress = 2;
        public const int GrabModeAsync = 1;
        public const long KeyPressMask = 1L << 0;
        public const uint ShiftMask = 1 << 0;
        public const uint LockMask = 1 << 1;
        public const uint ControlMask = 1 << 2;
        public const uint Mod1Mask = 1 << 3;
        public const uint Mod2Mask = 1 << 4;
        public const uint Mod4Mask = 1 << 6;

        [DllImport(LibX11)] public static extern IntPtr XOpenDisplay(IntPtr display);
        [DllImport(LibX11)] public static extern int XCloseDisplay(IntPtr display);
        [DllImport(LibX11)] public static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport(LibX11)] public static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, bool ownerEvents, int pointerMode, int keyboardMode);
        [DllImport(LibX11)] public static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);
        [DllImport(LibX11)] public static extern IntPtr XStringToKeysym(string str);
        [DllImport(LibX11)] public static extern int XKeysymToKeycode(IntPtr display, IntPtr keysym);
        [DllImport(LibX11)] public static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);
        [DllImport(LibX11)] public static extern int XNextEvent(IntPtr display, out XEvent xevent);
        [DllImport(LibX11)] public static extern int XPending(IntPtr display);
        [DllImport(LibX11)] public static extern int XFlush(IntPtr display);
        [DllImport(LibX11)] public static extern int XSync(IntPtr display, bool discard);
        [DllImport(LibX11)] private static extern IntPtr XSetErrorHandler(IntPtr handler);

        public static int TrapErrors(IntPtr display, Action action)
        {
            lock (SyncRoot)
            {
                _lastError = 0;
                var previous = XSetErrorHandler(ErrorHandlerPtr);
                try
                {
                    action();
                    XSync(display, false);
                    return _lastError;
                }
                finally
                {
                    XSetErrorHandler(previous);
                }
            }
        }

        private static int CaptureError(IntPtr display, ref XErrorEvent error)
        {
            _lastError = error.error_code;
            return 0;
        }

        private delegate int XErrorHandler(IntPtr display, ref XErrorEvent error);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XErrorEvent
    {
        public int type;
        public IntPtr display;
        public IntPtr resourceid;
        public IntPtr serial;
        public byte error_code;
        public byte request_code;
        public byte minor_code;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XKeyEvent
    {
        public int type;
        public IntPtr serial;
        public bool send_event;
        public IntPtr display;
        public IntPtr window;
        public IntPtr root;
        public IntPtr subwindow;
        public IntPtr time;
        public int x;
        public int y;
        public int x_root;
        public int y_root;
        public uint state;
        public uint keycode;
        public bool same_screen;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(0)] public XKeyEvent KeyEvent;
    }
}

internal sealed class PortalDesktopService
{
    public DBusConnection Connection { get; }
    public string Destination { get; }

    public PortalDesktopService(DBusConnection connection, string destination)
        => (Connection, Destination) = (connection, destination);

    public PortalGlobalShortcuts CreateGlobalShortcuts(ObjectPath path) => new(this, path);
    public PortalRequest CreateRequest(ObjectPath path) => new(this, path);
}

internal abstract class PortalObject
{
    protected PortalObject(PortalDesktopService service, ObjectPath path)
        => (Service, Path) = (service, path);

    public PortalDesktopService Service { get; }
    public ObjectPath Path { get; }
    protected DBusConnection Connection => Service.Connection;

    protected MessageBuffer CreateGetPropertyMessage(string @interface, string property)
    {
        using var writer = Connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: Service.Destination,
            path: Path,
            @interface: "org.freedesktop.DBus.Properties",
            signature: "ss",
            member: "Get");
        writer.WriteString(@interface);
        writer.WriteString(property);
        return writer.CreateMessage();
    }

    protected ValueTask<IDisposable> WatchSignalAsync<TArg>(string sender, string @interface, ObjectPath path, string signal, MessageValueReader<TArg> reader, Action<Exception?, TArg> handler, bool emitOnCapturedContext)
    {
        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Sender = sender,
            Path = path,
            Interface = @interface,
            Member = signal,
        };

        return Connection.AddMatchAsync(
            rule,
            reader,
            (Exception? ex, TArg arg, object? _, object? hs) => ((Action<Exception?, TArg>)hs!).Invoke(ex, arg),
            ObserverFlags.None,
            this,
            handler,
            emitOnCapturedContext);
    }

    protected static ObjectPath ReadMessage_o(Message message, PortalObject _)
    {
        var reader = message.GetBodyReader();
        return reader.ReadObjectPath();
    }

    protected static uint ReadMessage_v_u(Message message, PortalObject _)
    {
        var reader = message.GetBodyReader();
        reader.ReadSignature("u");
        return reader.ReadUInt32();
    }

    protected static (uint Response, Dictionary<string, VariantValue> Results) ReadMessage_uaesv(Message message, PortalObject _)
    {
        var reader = message.GetBodyReader();
        return (reader.ReadUInt32(), reader.ReadDictionaryOfStringToVariantValue());
    }

    protected static (ObjectPath SessionHandle, string ShortcutId, ulong Timestamp, Dictionary<string, VariantValue> Options) ReadMessage_ostaesv(Message message, PortalObject _)
    {
        var reader = message.GetBodyReader();
        return (reader.ReadObjectPath(), reader.ReadString(), reader.ReadUInt64(), reader.ReadDictionaryOfStringToVariantValue());
    }
}

internal sealed class PortalGlobalShortcuts : PortalObject
{
    private const string Interface = "org.freedesktop.portal.GlobalShortcuts";

    public PortalGlobalShortcuts(PortalDesktopService service, ObjectPath path) : base(service, path) { }

    public Task<uint> GetVersionAsync()
        => Connection.CallMethodAsync(CreateGetPropertyMessage(Interface, "version"), (Message m, object? s) => ReadMessage_v_u(m, (PortalObject)s!), this);

    public Task<ObjectPath> CreateSessionAsync(Dictionary<string, VariantValue> options)
    {
        return Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_o(m, (PortalObject)s!), this);

        MessageBuffer CreateMessage()
        {
            using var writer = Connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: Service.Destination,
                path: Path,
                @interface: Interface,
                signature: "a{sv}",
                member: "CreateSession");
            writer.WriteDictionary(options);
            return writer.CreateMessage();
        }
    }

    public Task<ObjectPath> BindShortcutsAsync(ObjectPath sessionHandle, (string Id, Dictionary<string, VariantValue> Properties)[] shortcuts, string parentWindow, Dictionary<string, VariantValue> options)
    {
        return Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_o(m, (PortalObject)s!), this);

        MessageBuffer CreateMessage()
        {
            using var writer = Connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: Service.Destination,
                path: Path,
                @interface: Interface,
                signature: "oa(sa{sv})sa{sv}",
                member: "BindShortcuts");
            writer.WriteObjectPath(sessionHandle);
            var arrayStart = writer.WriteArrayStart(DBusType.Struct);
            foreach (var shortcut in shortcuts)
            {
                writer.WriteStructureStart();
                writer.WriteString(shortcut.Id);
                writer.WriteDictionary(shortcut.Properties);
            }
            writer.WriteArrayEnd(arrayStart);
            writer.WriteString(parentWindow);
            writer.WriteDictionary(options);
            return writer.CreateMessage();
        }
    }

    public ValueTask<IDisposable> WatchActivatedAsync(Action<Exception?, (ObjectPath SessionHandle, string ShortcutId, ulong Timestamp, Dictionary<string, VariantValue> Options)> handler, bool emitOnCapturedContext = true)
        => WatchSignalAsync(Service.Destination, Interface, Path, "Activated", (Message m, object? s) => ReadMessage_ostaesv(m, (PortalObject)s!), handler, emitOnCapturedContext);
}

internal sealed class PortalRequest : PortalObject
{
    private const string Interface = "org.freedesktop.portal.Request";

    public PortalRequest(PortalDesktopService service, ObjectPath path) : base(service, path) { }

    public ValueTask<IDisposable> WatchResponseAsync(Action<Exception?, (uint Response, Dictionary<string, VariantValue> Results)> handler, bool emitOnCapturedContext = true)
        => WatchSignalAsync(Service.Destination, Interface, Path, "Response", (Message m, object? s) => ReadMessage_uaesv(m, (PortalObject)s!), handler, emitOnCapturedContext);
}
