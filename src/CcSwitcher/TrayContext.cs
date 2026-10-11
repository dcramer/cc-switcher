namespace CcSwitcher;

/// <summary>
/// CC Switcher while it is resident: an icon in the notification area whose menu launches
/// profiles, the profile list on request, and the styling of Claude's windows in between.
/// </summary>
sealed class TrayContext : ApplicationContext
{
    readonly Settings _settings;
    readonly SingleInstance _instance;
    readonly WindowStyler _styler;
    readonly ContextMenuStrip _menu = new();
    readonly NotifyIcon _tray;
    MainForm? _window;

    public TrayContext(Settings settings, SingleInstance instance, bool showWindow)
    {
        _settings = settings;
        _instance = instance;
        _styler = new WindowStyler(settings);

        _menu.Opening += (_, e) =>
        {
            BuildMenu();
            // An empty menu, which this one is until now, cancels its own opening.
            e.Cancel = false;
        };
        _tray = new NotifyIcon
        {
            Icon = AppIcon(SystemInformation.SmallIconSize.Width),
            Text = "CC Switcher",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowWindow();
        };

        instance.ShowRequested += ShowWindow;
        instance.ClaudeLaunched += _styler.ExpectWindow;
        instance.SessionEnding += ExitThread;

        if (showWindow)
            ShowWindow();
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _styler.Dispose();
        base.ExitThreadCore();
    }

    void ShowWindow()
    {
        if (_window is null)
        {
            _window = new MainForm(_settings, _styler);
            _window.FormClosed += (_, _) => _window = null;
            _window.Show();
        }
        if (_window.WindowState == FormWindowState.Minimized)
            _window.WindowState = FormWindowState.Normal;
        _window.Activate();
    }

    void BuildMenu()
    {
        foreach (var item in _menu.Items.Cast<ToolStripItem>().ToList())
        {
            item.Image?.Dispose();
            item.Dispose();
        }

        var iconSize = 16 * _menu.DeviceDpi / 96;
        _menu.ImageScalingSize = new Size(iconSize, iconSize);
        foreach (var profile in _settings.Profiles)
        {
            _menu.Items.Add(new ToolStripMenuItem(profile.Name, IconFactory.Render(_settings, iconSize, profile), (_, _) => Launch(profile))
            {
                // Shown where a shortcut key would be: right-aligned and muted.
                ShortcutKeyDisplayString = ClaudeApp.IsRunning(profile) ? "Running" : null,
            });
        }
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Open CC Switcher", null, (_, _) => ShowWindow());
        _menu.Items.Add("Exit", null, (_, _) => ExitThread());
    }

    void Launch(Profile profile)
    {
        try
        {
            ClaudeApp.Launch(_settings, profile);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't launch Claude:\n{ex.Message}", "CC Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _settings.LastProfileId = profile.Id;
        _settings.Save();
        _styler.ExpectWindow();
    }

    static Icon AppIcon(int size) =>
        IconFactory.Extract(Environment.ProcessPath!, size) ?? SystemIcons.Application;
}
