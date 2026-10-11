using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CcSwitcher;

/// <summary>
/// Routes claude:// links to a chosen profile. Claude's sign-in (Google, SSO, emailed link)
/// finishes through such a link, and Windows otherwise always hands it to the default profile.
/// </summary>
static class LinkRouter
{
    public enum State
    {
        NotRegistered,
        /// <summary>Offered to Windows as a handler, but the user hasn't picked it yet.</summary>
        Registered,
        Active,
    }

    const string AppName = "CC Switcher";
    const string ProgId = "CcSwitcher.claude";
    const string ProgIdKey = @"Software\Classes\" + ProgId;
    const string AppKey = @"Software\CcSwitcher";
    const string CapabilitiesKey = AppKey + @"\Capabilities";
    const string RegisteredAppsKey = @"Software\RegisteredApplications";
    const string UserChoiceKey = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\claude";

    public static State GetState()
    {
        using var progId = Registry.CurrentUser.OpenSubKey(ProgIdKey);
        if (progId is null)
            return State.NotRegistered;

        return UserChoice("UserChoiceLatest\\ProgId") == ProgId || UserChoice("UserChoice") == ProgId
            ? State.Active
            : State.Registered;
    }

    static string? UserChoice(string subKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{UserChoiceKey}\{subKey}");
        return key?.GetValue("ProgId") as string;
    }

    // Claude rewrites HKCU\Software\Classes\claude on every start, so taking that key over
    // wouldn't stick. Registering as a candidate handler and letting the user pick it in
    // Windows Settings records a UserChoice, which takes precedence and survives.
    public static void Register()
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Couldn't determine where CC Switcher is running from.");

        using (var progId = Registry.CurrentUser.CreateSubKey(ProgIdKey))
        {
            progId.SetValue(null, "URL:Claude link (CC Switcher)");
            progId.SetValue("URL Protocol", "");
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"\"{exe}\",0");
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(null, $"\"{exe}\" --url \"%1\"");
        }

        using (var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
        {
            capabilities.SetValue("ApplicationName", AppName);
            capabilities.SetValue("ApplicationDescription", "Opens Claude links in the profile you choose.");
            using var urls = capabilities.CreateSubKey("URLAssociations");
            urls.SetValue("claude", ProgId);
        }

        using (var registered = Registry.CurrentUser.CreateSubKey(RegisteredAppsKey))
            registered.SetValue(AppName, CapabilitiesKey);

        NotifyAssociationsChanged();
    }

    public static void Unregister()
    {
        using (var registered = Registry.CurrentUser.OpenSubKey(RegisteredAppsKey, writable: true))
            registered?.DeleteValue(AppName, throwOnMissingValue: false);

        Registry.CurrentUser.DeleteSubKeyTree(AppKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ProgIdKey, throwOnMissingSubKey: false);
        NotifyAssociationsChanged();
    }

    /// <summary>Keeps the registered command pointing at this exe if it has moved.</summary>
    public static void RefreshRegistration()
    {
        if (GetState() != State.NotRegistered)
            Register();
    }

    public static void OpenWindowsSettings()
    {
        var uri = "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(AppName);
        using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    public static void Route(Settings settings, string url)
    {
        // The link comes from whatever asked Windows to open it, so treat it as untrusted:
        // it must be a claude: URL, and it is only ever passed on as a single argument.
        if (!url.StartsWith("claude:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CC Switcher only routes claude:// links.");

        var profiles = settings.Profiles;
        if (profiles.Count == 0)
            throw new InvalidOperationException("There are no profiles to open this link in.");

        var running = profiles.Where(ClaudeApp.IsRunning).ToList();
        Profile? target;
        if (profiles.Count == 1)
        {
            target = profiles[0];
        }
        else if (running.Count == 1)
        {
            // A sign-in link can only answer a window that is open, so one running profile is unambiguous.
            target = running[0];
        }
        else
        {
            using var picker = new LinkPickerDialog(settings, Describe(url), running);
            target = picker.ShowDialog() == DialogResult.OK ? picker.Selected : null;
        }

        if (target is not null)
            ClaudeApp.Launch(settings, target, url);
    }

    // Describes the link without showing it; sign-in links carry one-time credentials.
    static string Describe(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "Claude link";

        var where = (uri.Host + uri.AbsolutePath).ToLowerInvariant();
        if (where.StartsWith("login") || where.Contains("magic-link") || where.Contains("sso-callback") || where.Contains("google-auth"))
            return "sign-in link";
        if (where.Contains("mcp-auth-callback"))
            return "connector sign-in link";
        if (where.StartsWith("code"))
            return "Claude Code link";
        return "Claude link";
    }

    static void NotifyAssociationsChanged() =>
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    const int SHCNE_ASSOCCHANGED = 0x08000000;
    const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
