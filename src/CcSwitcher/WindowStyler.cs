using System.Runtime.InteropServices;

namespace CcSwitcher;

/// <summary>
/// Makes each profile's Claude windows recognizable: their own taskbar button, the profile's
/// tinted icon and a border in its color. Claude has no setting for any of this, so it is applied
/// to its windows from outside, and has to be kept applied for as long as they are open.
/// </summary>
sealed class WindowStyler : IDisposable
{
    const int IdleInterval = 2000;
    const int EagerInterval = 250;

    sealed class Tracked
    {
        public int ProcessId;
        public Profile? Profile;
        public bool Styled;
    }

    readonly record struct WindowIcons(IntPtr Big, IntPtr Small);

    readonly Settings _settings;
    readonly Dictionary<IntPtr, Tracked> _windows = [];
    readonly Dictionary<string, WindowIcons?> _icons = [];
    readonly System.Windows.Forms.Timer _timer = new() { Interval = IdleInterval };
    // Held in a field: Windows calls it for as long as the hook is installed.
    readonly WinEventProc _onForeground;
    readonly IntPtr _foregroundHook;
    long _eagerUntil;

    public WindowStyler(Settings settings)
    {
        _settings = settings;
        _timer.Tick += (_, _) =>
        {
            Scan();
            if (_timer.Interval == EagerInterval && Environment.TickCount64 > _eagerUntil)
                _timer.Interval = IdleInterval;
        };
        _timer.Start();

        // A new window takes the foreground, so this notices one sooner than the timer would.
        _onForeground = (_, _, _, _, _, _, _) => Scan();
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _onForeground, 0, 0, WINEVENT_OUTOFCONTEXT);

        Scan();
    }

    /// <summary>Checks for new windows often for a while. Call after starting Claude, whose window takes a few seconds to appear.</summary>
    public void ExpectWindow()
    {
        _eagerUntil = Environment.TickCount64 + 20_000;
        _timer.Interval = EagerInterval;
    }

    /// <summary>Picks up changes to profiles: renames, new colors.</summary>
    public void Refresh()
    {
        foreach (var tracked in _windows.Values)
            tracked.Profile = ClaudeApp.ProfileOf(_settings, tracked.ProcessId);

        // Windows keep showing the old icons until the new ones are set, so destroy them last.
        var stale = _icons.Values.ToList();
        _icons.Clear();
        Scan(identityChanged: true);
        DestroyIcons(stale);
    }

    public void Dispose()
    {
        _timer.Dispose();
        if (_foregroundHook != IntPtr.Zero)
            UnhookWinEvent(_foregroundHook);

        // The icons die with this process, so don't leave windows pointing at them.
        foreach (var (window, tracked) in _windows)
        {
            if (tracked.Styled)
                Restore(window);
        }
        _windows.Clear();
        DestroyIcons(_icons.Values);
        _icons.Clear();
    }

    void Scan(bool identityChanged = false)
    {
        var open = ClaudeApp.FindWindows();
        // A window that is only hidden (Claude closed to its tray icon) is still styled, so it stays tracked.
        foreach (var closed in _windows.Keys.Where(window => !IsWindow(window)).ToList())
            _windows.Remove(closed);

        foreach (var window in open)
        {
            // Handles are reused, so a known handle only counts if it's still the same process.
            var processId = ClaudeApp.ProcessOf(window);
            if (!_windows.TryGetValue(window, out var tracked) || tracked.ProcessId != processId)
                _windows[window] = tracked = new Tracked { ProcessId = processId, Profile = ClaudeApp.ProfileOf(_settings, processId) };

            // A profile in Claude's own color is left exactly as Claude made it.
            if (tracked.Profile is { } profile && !IconFactory.IsStockColor(profile))
            {
                tracked.Styled |= Apply(window, profile, identityChanged || !tracked.Styled);
            }
            else if (tracked.Styled)
            {
                Restore(window);
                tracked.Styled = false;
            }
        }
    }

    bool Apply(IntPtr window, Profile profile, bool setIdentity)
    {
        if (IconsFor(profile) is not { } icons)
            return false;

        // Claude gives every window the same taskbar ID, which stacks all profiles under one
        // button. A window's own ID overrides it, and the relaunch details tell the taskbar
        // what that button is and what pinning it should start.
        var id = TaskbarIdPrefix + profile.Id;
        if (setIdentity || TaskbarProperty(window, PKEY_ID) != id)
        {
            SetTaskbarProperty(window, PKEY_ID, id);
            SetTaskbarProperty(window, PKEY_RelaunchIcon, profile.IconPath + ",0");
            SetTaskbarProperty(window, PKEY_RelaunchName, Shortcuts.DisplayName(profile));
            SetTaskbarProperty(window, PKEY_RelaunchCommand, $"\"{Environment.ProcessPath}\" {Shortcuts.Arguments(profile)}");
        }

        if (!Send(window, WM_GETICON, ICON_BIG, IntPtr.Zero, out var current) || current != icons.Big)
            SetIcons(window, icons);

        // Borderless helper windows (Quick Entry is one) would get an outline around nothing.
        if ((GetWindowLong(window, GWL_STYLE) & WS_THICKFRAME) != 0)
            SetBorder(window, ToColorRef(IconFactory.ParseColor(profile.Color)));
        return true;
    }

    static void Restore(IntPtr window)
    {
        SetIcons(window, default);
        SetBorder(window, DWMWA_COLOR_DEFAULT);
        SetTaskbarProperty(window, PKEY_RelaunchCommand, null);
        SetTaskbarProperty(window, PKEY_RelaunchName, null);
        SetTaskbarProperty(window, PKEY_RelaunchIcon, null);
        SetTaskbarProperty(window, PKEY_ID, null);
    }

    WindowIcons? IconsFor(Profile profile)
    {
        if (_icons.TryGetValue(profile.Id, out var known))
            return known;

        WindowIcons? icons = null;
        try
        {
            IconFactory.WriteIco(_settings, profile.IconPath, profile);
            // The taskbar and Alt+Tab scale the large icon down themselves and look better with more to work from.
            var big = Math.Max(64, GetSystemMetrics(SM_CXICON));
            var small = GetSystemMetrics(SM_CXSMICON);
            icons = new WindowIcons(
                LoadImage(IntPtr.Zero, profile.IconPath, IMAGE_ICON, big, big, LR_LOADFROMFILE),
                LoadImage(IntPtr.Zero, profile.IconPath, IMAGE_ICON, small, small, LR_LOADFROMFILE));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Remembered as missing below, so a profile whose icon can't be written isn't retried every scan.
        }

        if (icons is { Big: 0 } or { Small: 0 })
        {
            DestroyIcons([icons]);
            icons = null;
        }
        return _icons[profile.Id] = icons;
    }

    static void DestroyIcons(IEnumerable<WindowIcons?> icons)
    {
        foreach (var pair in icons)
        {
            if (pair is not { } loaded)
                continue;
            if (loaded.Big != IntPtr.Zero)
                DestroyIcon(loaded.Big);
            if (loaded.Small != IntPtr.Zero)
                DestroyIcon(loaded.Small);
        }
    }

    static void SetIcons(IntPtr window, WindowIcons icons)
    {
        Send(window, WM_SETICON, ICON_BIG, icons.Big, out _);
        Send(window, WM_SETICON, ICON_SMALL, icons.Small, out _);
    }

    // With a timeout, so a Claude window that has stopped responding can't hang us too.
    static bool Send(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, out IntPtr result) =>
        SendMessageTimeout(window, message, wParam, lParam, SMTO_ABORTIFHUNG, 200, out result) != IntPtr.Zero;

    // Windows 11 only; earlier versions reject the attribute and keep their own border.
    static void SetBorder(IntPtr window, uint color) =>
        DwmSetWindowAttribute(window, DWMWA_BORDER_COLOR, ref color, sizeof(uint));

    static uint ToColorRef(Color color) => (uint)(color.R | color.G << 8 | color.B << 16);

    static string? TaskbarProperty(IntPtr window, PropertyKey key)
    {
        if (SHGetPropertyStoreForWindow(window, typeof(IPropertyStore).GUID, out var store) != 0)
            return null;
        try
        {
            store.GetValue(ref key, out var value);
            var text = value.Type == VT_LPWSTR ? Marshal.PtrToStringUni(value.Pointer) : null;
            PropVariantClear(ref value);
            return text;
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>Sets one of a window's taskbar properties, or removes it when the value is null.</summary>
    static void SetTaskbarProperty(IntPtr window, PropertyKey key, string? text)
    {
        if (SHGetPropertyStoreForWindow(window, typeof(IPropertyStore).GUID, out var store) != 0)
            return;
        try
        {
            var value = new PropVariant();
            if (text is not null)
            {
                value.Type = VT_LPWSTR;
                value.Pointer = Marshal.StringToCoTaskMemUni(text);
            }
            store.SetValue(ref key, ref value);
            PropVariantClear(ref value);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    const string TaskbarIdPrefix = "CcSwitcher.Claude.";

    // System.AppUserModel.*: the properties the taskbar reads from a window.
    static readonly Guid AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    static readonly PropertyKey PKEY_RelaunchCommand = new(AppUserModel, 2);
    static readonly PropertyKey PKEY_RelaunchIcon = new(AppUserModel, 3);
    static readonly PropertyKey PKEY_RelaunchName = new(AppUserModel, 4);
    static readonly PropertyKey PKEY_ID = new(AppUserModel, 5);

    const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    const uint WINEVENT_OUTOFCONTEXT = 0;
    const uint WM_GETICON = 0x007F;
    const uint WM_SETICON = 0x0080;
    const nint ICON_SMALL = 0;
    const nint ICON_BIG = 1;
    const uint SMTO_ABORTIFHUNG = 0x0002;
    const uint IMAGE_ICON = 1;
    const uint LR_LOADFROMFILE = 0x0010;
    const int SM_CXICON = 11;
    const int SM_CXSMICON = 49;
    const int GWL_STYLE = -16;
    const int WS_THICKFRAME = 0x00040000;
    const int DWMWA_BORDER_COLOR = 34;
    const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;
    const ushort VT_LPWSTR = 31;

    delegate void WinEventProc(IntPtr hook, uint eventId, IntPtr window, int objectId, int childId, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

    // PROPVARIANT, of which only the string form is used.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll")]
    static extern int SHGetPropertyStoreForWindow(IntPtr window, in Guid interfaceId, out IPropertyStore store);

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref uint value, int size);

    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);
}
