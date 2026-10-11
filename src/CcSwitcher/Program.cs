namespace CcSwitcher;

static class Program
{
    const string Usage =
        "cc-switcher                     Open the profile list\n" +
        "cc-switcher --profile <name>    Open Claude with that profile\n" +
        "cc-switcher --url <claude://…>  Open a Claude link in a profile\n" +
        "cc-switcher --tray              Start in the notification area";

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);
        try
        {
            var settings = Settings.Load();
            switch (args)
            {
                case []:
                    RefreshLinkRegistration();
                    StayResident(settings, showWindow: true);
                    return 0;

                case ["--tray"]:
                    StayResident(settings, showWindow: false);
                    return 0;

                case ["--profile", var name]:
                    var profile = settings.Find(name)
                        ?? throw new InvalidOperationException($"There's no profile named \"{name}\". It may have been removed.");
                    ClaudeApp.Launch(settings, profile);
                    settings.LastProfileId = profile.Id;
                    settings.Save();
                    StayResident(settings, showWindow: false);
                    return 0;

                case ["--url", var url]:
                    LinkRouter.Route(settings, url);
                    StayResident(settings, showWindow: false);
                    return 0;

                default:
                    MessageBox.Show(Usage, "CC Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CC Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    // Claude's windows only stay styled while CC Switcher is running, so whichever launch finds
    // nobody resident stays on in the notification area; any other reports in and leaves.
    static void StayResident(Settings settings, bool showWindow)
    {
        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SingleInstance.Notify(showWindow);
            return;
        }

        using var context = new TrayContext(settings, instance, showWindow);
        Application.Run(context);
    }

    static void RefreshLinkRegistration()
    {
        try
        {
            LinkRouter.RefreshRegistration();
        }
        catch (Exception)
        {
            // Link routing is optional; the main window reports its state.
        }
    }
}
