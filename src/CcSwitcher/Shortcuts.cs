namespace CcSwitcher;

static class Shortcuts
{
    static readonly Environment.SpecialFolder[] Locations =
        [Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs];

    /// <summary>Creates a "Claude (Name)" shortcut that opens the profile directly.</summary>
    public static string Create(Profile profile, Environment.SpecialFolder folder)
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Couldn't determine where CC Switcher is running from.");

        var icon = IconPath(profile);
        IconFactory.WriteIco(icon, profile);

        var name = string.Concat(profile.Name.Split(Path.GetInvalidFileNameChars())).Trim();
        var path = Path.Combine(Environment.GetFolderPath(folder), $"Claude ({name}).lnk");

        dynamic shortcut = Shell().CreateShortcut(path);
        shortcut.TargetPath = exe;
        shortcut.Arguments = Arguments(profile);
        shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
        shortcut.IconLocation = icon + ",0";
        shortcut.Description = $"Open Claude with the {profile.Name} profile";
        shortcut.Save();
        return path;
    }

    /// <summary>Deletes the shortcuts made for a profile, wherever a rename has left them. Best effort.</summary>
    public static void Remove(Profile profile)
    {
        try
        {
            var shell = Shell();
            foreach (var location in Locations)
            {
                foreach (var path in Directory.GetFiles(Environment.GetFolderPath(location), "Claude (*).lnk"))
                {
                    // Only ours: identified by the profile it launches, not by its file name.
                    dynamic shortcut = shell.CreateShortcut(path);
                    if ((string)shortcut.Arguments == Arguments(profile))
                        File.Delete(path);
                }
            }
            File.Delete(IconPath(profile));
        }
        catch (Exception)
        {
            // A leftover shortcut just reports that its profile is gone.
        }
    }

    static string Arguments(Profile profile) => $"--profile \"{profile.Id}\"";

    static string IconPath(Profile profile) => Path.Combine(Settings.IconDir, profile.Id + ".ico");

    static dynamic Shell()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable, so shortcuts can't be managed.");
        return Activator.CreateInstance(type)!;
    }
}
