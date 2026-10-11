namespace CcSwitcher;

static class Program
{
    const string Usage =
        "cc-switcher                     Open the profile list\n" +
        "cc-switcher --profile <name>    Open Claude with that profile\n" +
        "cc-switcher --url <claude://…>  Open a Claude link in a profile";

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var settings = Settings.Load();
            switch (args)
            {
                case []:
                    RefreshLinkRegistration();
                    Application.Run(new MainForm(settings));
                    return 0;

                case ["--profile", var name]:
                    var profile = settings.Find(name)
                        ?? throw new InvalidOperationException($"There's no profile named \"{name}\". It may have been removed.");
                    ClaudeApp.Launch(settings, profile);
                    settings.LastProfileId = profile.Id;
                    settings.Save();
                    return 0;

                case ["--url", var url]:
                    LinkRouter.Route(settings, url);
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
