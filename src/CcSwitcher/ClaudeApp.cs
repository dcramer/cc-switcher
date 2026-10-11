using System.Diagnostics;
using Microsoft.Win32;

namespace CcSwitcher;

/// <summary>Finds and launches Claude Desktop, one instance per profile.</summary>
static class ClaudeApp
{
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
}
