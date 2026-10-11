using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace CcSwitcher;

/// <summary>Finds and launches Claude Desktop, one instance per profile.</summary>
static class ClaudeApp
{
    const string PackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    public static string? FindExecutable(Settings settings)
    {
        foreach (var candidate in ExecutableCandidates(settings))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    static IEnumerable<string?> ExecutableCandidates(Settings settings)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        yield return settings.ClaudePath;
        // MSIX install: the package's app execution alias. Its path survives updates.
        yield return Path.Combine(local, "Microsoft", "WindowsApps", "claude-desktop.exe");
        // Legacy Squirrel install.
        yield return Path.Combine(local, "AnthropicClaude", "claude.exe");
        // Whatever Claude last registered itself as for claude:// links.
        yield return ProtocolHandlerExecutable();
    }

    /// <summary>Files that may hold Claude's icon. The MSIX alias is a stub without one, so the real executable behind it is tried too.</summary>
    public static IEnumerable<string> IconSources(Settings settings) =>
        ExecutableCandidates(settings).Concat(PackagedExecutables()).Where(File.Exists)!;

    static List<string> PackagedExecutables()
    {
        var found = new List<string>();
        using var packages = Registry.CurrentUser.OpenSubKey(PackagesKey);
        foreach (var name in packages?.GetSubKeyNames() ?? [])
        {
            if (!name.StartsWith("Claude_", StringComparison.OrdinalIgnoreCase))
                continue;
            using var package = packages!.OpenSubKey(name);
            if (package?.GetValue("PackageRootFolder") is string root)
                found.Add(Path.Combine(root, "app", "claude.exe"));
        }
        return found;
    }

    static string? ProtocolHandlerExecutable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\claude\shell\open\command");
        if (key?.GetValue(null) is not string command || !command.StartsWith('"'))
            return null;

        var end = command.IndexOf('"', 1);
        if (end < 0)
            return null;

        var path = command[1..end];
        return Path.GetFileName(path).Equals("claude.exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    public static void Launch(Settings settings, Profile profile, string? url = null)
    {
        var exe = FindExecutable(settings)
            ?? throw new FileNotFoundException("Couldn't find Claude Desktop. Is it installed?");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        // Signed builds ignore CLAUDE_USER_DATA_DIR, so the profile is selected with Chromium's
        // --user-data-dir switch. Claude's single-instance lock is per data folder, which is
        // what lets profiles run side by side (and makes a relaunch focus the right window).
        if (!profile.UsesDefaultData)
        {
            Directory.CreateDirectory(profile.DataDir);
            psi.ArgumentList.Add($"--user-data-dir={profile.DataDir}");
        }

        if (profile.SeparateCodeConfig)
        {
            Directory.CreateDirectory(profile.CodeConfigDir);
            psi.Environment["CLAUDE_CONFIG_DIR"] = profile.CodeConfigDir;
        }

        if (url is not null)
            psi.ArgumentList.Add(url);

        using var process = Process.Start(psi);
    }

    public static bool IsRunning(Profile profile) =>
        DataDirCandidates(profile).Any(dir => IsLocked(Path.Combine(dir, "lockfile")));

    /// <summary>The folder holding a profile's sign-in; for the default profile, whichever candidate exists.</summary>
    public static string ResolveDataDir(Profile profile)
    {
        var candidates = DataDirCandidates(profile).ToList();
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    static IEnumerable<string> DataDirCandidates(Profile profile)
    {
        if (!profile.UsesDefaultData)
        {
            yield return profile.DataDir;
            yield break;
        }

        // Where the default profile lives depends on the install type and Claude version.
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        yield return Path.Combine(roaming, "Claude");

        var moved = Path.Combine(local, "Claude-Data");
        yield return moved;
        foreach (var dir in SafeDirectories(moved, "*"))
            yield return dir;

        foreach (var package in SafeDirectories(Path.Combine(local, "Packages"), "Claude_*"))
            yield return Path.Combine(package, "LocalCache", "Roaming", "Claude");
    }

    /// <summary>Handles of the open Claude windows that have a taskbar button.</summary>
    public static List<IntPtr> FindWindows()
    {
        var windows = new List<IntPtr>();
        var className = new StringBuilder(64);
        EnumWindows((window, _) =>
        {
            // Every Electron and Chrome window has this class, so the process is checked too (in ProfileOf).
            if (IsWindowVisible(window)
                && GetWindow(window, GW_OWNER) == IntPtr.Zero
                && (GetWindowLong(window, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) == 0
                && GetClassName(window, className, className.Capacity) > 0
                && className.ToString() == "Chrome_WidgetWin_1")
            {
                windows.Add(window);
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    /// <summary>The id of the process that owns a window.</summary>
    public static int ProcessOf(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    /// <summary>The profile a process is running, or null if it isn't Claude Desktop or uses a folder we don't manage.</summary>
    public static Profile? ProfileOf(Settings settings, int processId)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero)
            return null;
        try
        {
            var path = new StringBuilder(1024);
            var length = path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref length)
                || !Path.GetFileName(path.ToString()).Equals("claude.exe", StringComparison.OrdinalIgnoreCase)
                || CommandLine(process) is not { } commandLine)
            {
                return null;
            }

            // The same switch Launch passes; without it Claude is using its default folder.
            const string dataDirSwitch = "--user-data-dir=";
            return commandLine.Contains(dataDirSwitch, StringComparison.OrdinalIgnoreCase)
                ? settings.Profiles.FirstOrDefault(p => !p.UsesDefaultData && commandLine.Contains(dataDirSwitch + p.DataDir, StringComparison.OrdinalIgnoreCase))
                : settings.Profiles.FirstOrDefault(p => p.UsesDefaultData);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    static string? CommandLine(IntPtr process)
    {
        NtQueryInformationProcess(process, ProcessCommandLineInformation, IntPtr.Zero, 0, out var size);
        if (size <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, size, out _) != 0)
                return null;
            var text = Marshal.PtrToStructure<UnicodeString>(buffer);
            return Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    static string[] SafeDirectories(string parent, string pattern)
    {
        try
        {
            return Directory.Exists(parent) ? Directory.GetDirectories(parent, pattern) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // A running instance holds its lockfile open for writing and deletes it on exit, so a
    // lockfile we can't open exclusively means the profile is live; one we can is stale.
    static bool IsLocked(string lockfile)
    {
        try
        {
            if (!File.Exists(lockfile))
                return false;
            using var stream = new FileStream(lockfile, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    const uint GW_OWNER = 4;
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOOLWINDOW = 0x80;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const int ProcessCommandLineInformation = 60;

    delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    static extern IntPtr GetWindow(IntPtr window, uint relation);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int length);

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr buffer, int size, out int needed);
}
