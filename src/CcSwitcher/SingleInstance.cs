using System.Runtime.InteropServices;

namespace CcSwitcher;

/// <summary>
/// Keeps one CC Switcher resident per session, and lets later launches hand their request to it
/// through a hidden window.
/// </summary>
sealed class SingleInstance : NativeWindow, IDisposable
{
    const string Name = "CcSwitcher.Instance";
    static readonly int ShowMessage = RegisterWindowMessage("CcSwitcher.Show");
    static readonly int LaunchedMessage = RegisterWindowMessage("CcSwitcher.Launched");

    readonly Mutex _mutex;

    /// <summary>Another launch asked for the profile list.</summary>
    public event Action? ShowRequested;
    /// <summary>Another launch started Claude.</summary>
    public event Action? ClaudeLaunched;
    /// <summary>Windows, or an installer replacing the app, is closing it.</summary>
    public event Action? SessionEnding;

    SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        CreateHandle(new CreateParams { Caption = Name });
    }

    /// <summary>Becomes the resident instance, or returns null if there already is one.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, Name, out var created);
        if (created)
            return new SingleInstance(mutex);
        mutex.Dispose();
        return null;
    }

    /// <summary>Tells the resident instance what this launch did, and optionally to show the profile list.</summary>
    public static void Notify(bool show)
    {
        var window = FindWindow(null, Name);
        if (window == IntPtr.Zero)
            return;

        // Only the foreground process may hand the foreground on, and that is us, just started by the user.
        GetWindowThreadProcessId(window, out var processId);
        AllowSetForegroundWindow(processId);
        PostMessage(window, show ? ShowMessage : LaunchedMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        DestroyHandle();
        _mutex.Dispose();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == ShowMessage)
            ShowRequested?.Invoke();
        else if (m.Msg == LaunchedMessage)
            ClaudeLaunched?.Invoke();
        else if (m.Msg == WM_ENDSESSION && m.WParam != IntPtr.Zero)
            SessionEnding?.Invoke();

        base.WndProc(ref m);
    }

    const int WM_ENDSESSION = 0x0016;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? className, string title);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
