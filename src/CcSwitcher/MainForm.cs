using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;

namespace CcSwitcher;

sealed class MainForm : Form
{
    readonly Settings _settings;
    readonly WindowStyler _styler;
    readonly ProfileList _list = new();
    readonly Button _launch = MakeButton("Launch");
    readonly Button _add = MakeButton("Add…");
    readonly Button _edit = MakeButton("Edit…");
    readonly Button _remove = MakeButton("Remove");
    readonly Button _shortcut = MakeButton("Shortcut ▾");
    readonly ContextMenuStrip _shortcutMenu = new();
    readonly Label _linkStatus = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 8, 0) };
    readonly Button _linkAction = MakeButton("");
    readonly Button _linkOff = MakeButton("Turn off");
    readonly CheckBox _closeAfterLaunch = new() { Text = "Close this window after launching", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
    readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 2000 };
    LinkRouter.State _linkState;

    public MainForm(Settings settings, WindowStyler styler)
    {
        _settings = settings;
        _styler = styler;

        SuspendLayout();
        Text = "CC Switcher";
        ClientSize = new Size(460, 340);
        MinimumSize = new Size(460, 300);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = IconFactory.App;

        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = false };
        buttons.Controls.AddRange([_launch, _add, _edit, _remove, _shortcut]);

        var linkButtons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = false };
        linkButtons.Controls.AddRange([_linkAction, _linkOff]);

        var linkRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0, 12, 0, 0) };
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linkRow.Controls.Add(_linkStatus, 0, 0);
        linkRow.Controls.Add(linkButtons, 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_list.Framed(new Padding(0, 0, 0, 8)), 0, 0);
        root.Controls.Add(buttons, 0, 1);
        root.Controls.Add(linkRow, 0, 2);
        root.Controls.Add(_closeAfterLaunch, 0, 3);
        Controls.Add(root);
        AcceptButton = _launch;

        _shortcutMenu.Items.Add("On the desktop", null, (_, _) => CreateShortcut(Environment.SpecialFolder.DesktopDirectory));
        _shortcutMenu.Items.Add("In the Start menu", null, (_, _) => CreateShortcut(Environment.SpecialFolder.Programs));

        var listMenu = new ContextMenuStrip();
        listMenu.Items.Add("Launch", null, (_, _) => LaunchSelected());
        listMenu.Items.Add("Edit…", null, (_, _) => EditSelected());
        listMenu.Items.Add("Open data folder", null, (_, _) => OpenDataFolder());
        listMenu.Items.Add(new ToolStripSeparator());
        var removeItem = listMenu.Items.Add("Remove", null, (_, _) => RemoveSelected());
        listMenu.Opening += (_, e) =>
        {
            e.Cancel = Selected is null;
            removeItem.Enabled = Selected is { UsesDefaultData: false };
        };
        _list.ContextMenuStrip = listMenu;

        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.RowActivated += LaunchSelected;
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
                RemoveSelected();
            else if (e.KeyCode == Keys.F2)
                EditSelected();
        };
        _launch.Click += (_, _) => LaunchSelected();
        _add.Click += (_, _) => AddProfile();
        _edit.Click += (_, _) => EditSelected();
        _remove.Click += (_, _) => RemoveSelected();
        _shortcut.Click += (_, _) => _shortcutMenu.Show(_shortcut, new Point(0, _shortcut.Height));
        _linkAction.Click += (_, _) => SetUpLinkRouting();
        _linkOff.Click += (_, _) => TurnOffLinkRouting();

        _closeAfterLaunch.Checked = settings.CloseAfterLaunch;
        _closeAfterLaunch.CheckedChanged += (_, _) =>
        {
            _settings.CloseAfterLaunch = _closeAfterLaunch.Checked;
            _settings.Save();
        };

        _refreshTimer.Tick += (_, _) => RefreshStatus();
        _refreshTimer.Start();

        ReloadList(settings.LastProfileId);

        // Sizes above are in 96-DPI units. Like designer-generated code, scale once at the end:
        // WinForms only applies DPI scaling to what's inside a SuspendLayout/ResumeLayout pair.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    string DefaultProfileName =>
        _settings.Profiles.FirstOrDefault(p => p.UsesDefaultData)?.Name ?? "Claude's default profile";

    Profile? Selected => _list.Selected;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _list.RevealSelection();
        _list.Select();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer.Dispose();
        base.OnFormClosed(e);
    }

    void ReloadList(string? selectId)
    {
        _list.SetProfiles(_settings, selectId);
        RefreshStatus();
        RefreshLinkState(force: true);
        UpdateButtons();
    }

    void RefreshStatus()
    {
        foreach (var profile in _settings.Profiles)
        {
            var running = ClaudeApp.IsRunning(profile);
            var text = running ? "Running"
                : profile.UsesDefaultData || Directory.Exists(profile.DataDir) ? ""
                : "New";
            _list.SetStatus(profile, text, running);
        }
        RefreshLinkState(force: false);
    }

    void UpdateButtons()
    {
        var profile = Selected;
        _launch.Enabled = _edit.Enabled = _shortcut.Enabled = profile is not null;
        // The default profile is Claude's own data, not ours to remove.
        _remove.Enabled = profile is { UsesDefaultData: false };
    }

    void LaunchSelected()
    {
        if (Selected is not { } profile)
            return;

        if (ClaudeApp.FindExecutable(_settings) is null && !LocateClaude())
            return;

        try
        {
            ClaudeApp.Launch(_settings, profile);
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't launch Claude:\n{ex.Message}");
            return;
        }

        _settings.LastProfileId = profile.Id;
        _settings.Save();
        _styler.ExpectWindow();
        if (_settings.CloseAfterLaunch)
            Close();
        else
            RefreshStatus();
    }

    bool LocateClaude()
    {
        MessageBox.Show(this, "Couldn't find Claude Desktop automatically. Choose its claude.exe in the next dialog.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        using var dialog = new OpenFileDialog { Title = "Locate Claude Desktop", Filter = "Claude (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return false;
        _settings.ClaudePath = dialog.FileName;
        _settings.Save();
        return true;
    }

    void AddProfile()
    {
        using var dialog = new ProfileDialog(_settings, null);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var profile = _settings.Add(dialog.ProfileName, dialog.ProfileColor, dialog.SeparateCodeConfig);
        _settings.Save();
        ReloadList(profile.Id);
    }

    void EditSelected()
    {
        if (Selected is not { } profile)
            return;
        using var dialog = new ProfileDialog(_settings, profile);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        profile.Name = dialog.ProfileName;
        profile.Color = dialog.ProfileColor;
        profile.SeparateCodeConfig = dialog.SeparateCodeConfig;
        _settings.Save();
        _styler.Refresh();
        ReloadList(profile.Id);
    }

    void RemoveSelected()
    {
        if (Selected is not { UsesDefaultData: false } profile)
            return;

        if (ClaudeApp.IsRunning(profile))
        {
            MessageBox.Show(this, $"Claude is still running with the \"{profile.Name}\" profile. Quit it first (right-click its tray icon and choose Quit), then remove the profile.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var hasData = Directory.Exists(profile.Folder);
        var remove = new TaskDialogButton("Remove");
        var page = new TaskDialogPage
        {
            Caption = Text,
            Heading = $"Remove the \"{profile.Name}\" profile?",
            Text = hasData
                ? "Its sign-in and local data will be moved to the Recycle Bin. Your Claude account itself isn't affected."
                : "It has never been launched, so there's no data to remove.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { remove, TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
        };
        if (TaskDialog.ShowDialog(this, page) != remove)
            return;

        try
        {
            RecycleProfileFolder(profile);
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't remove the profile's data, so the profile was kept:\n{ex.Message}");
            return;
        }

        Shortcuts.Remove(profile);
        _settings.Profiles.Remove(profile);
        if (_settings.LastProfileId == profile.Id)
            _settings.LastProfileId = null;
        _settings.Save();
        _styler.Refresh();
        ReloadList(null);
    }

    static void RecycleProfileFolder(Profile profile)
    {
        var folder = Path.GetFullPath(profile.Folder);
        // The id comes from a hand-editable file; never delete anything but a direct child of the profiles root.
        var parent = Path.GetDirectoryName(folder);
        if (profile.Id.Length == 0 || !string.Equals(parent, Path.GetFullPath(Settings.ProfilesRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"\"{folder}\" isn't a folder CC Switcher manages.");

        if (Directory.Exists(folder))
            FileSystem.DeleteDirectory(folder, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    void CreateShortcut(Environment.SpecialFolder folder)
    {
        if (Selected is not { } profile)
            return;
        try
        {
            var path = Shortcuts.Create(_settings, profile, folder);
            MessageBox.Show(this, $"Created \"{Path.GetFileNameWithoutExtension(path)}\". It opens Claude straight into this profile.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't create the shortcut:\n{ex.Message}");
        }
    }

    void OpenDataFolder()
    {
        if (Selected is not { } profile)
            return;
        var folder = ClaudeApp.ResolveDataDir(profile);
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this, "This profile has no data yet. Launch it once to create it.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var process = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    void RefreshLinkState(bool force)
    {
        var state = LinkRouter.GetState();
        if (!force && state == _linkState)
            return;
        _linkState = state;

        (_linkStatus.Text, _linkAction.Text) = state switch
        {
            LinkRouter.State.Active => ("Sign-in links are routed to the profile you choose.", ""),
            LinkRouter.State.Registered => ("Almost done: in Windows Settings, set the CLAUDE link type to CC Switcher.", "Open Settings"),
            _ => ($"Sign-in links (Google, SSO, email) only reach {DefaultProfileName} until you route them here.", "Set up…"),
        };
        _linkAction.Visible = state != LinkRouter.State.Active;
        _linkOff.Visible = state != LinkRouter.State.NotRegistered;
    }

    void SetUpLinkRouting()
    {
        if (_linkState == LinkRouter.State.NotRegistered)
        {
            var proceed = new TaskDialogButton("Register and open Settings");
            var page = new TaskDialogPage
            {
                Caption = Text,
                Heading = "Route Claude's sign-in links through CC Switcher?",
                Text = "When you sign in with Google, SSO or an emailed link, your browser hands the result back through a claude:// link. " +
                       "Windows always gives that link to Claude's default profile, so other profiles can't finish signing in.\n\n" +
                       "CC Switcher can receive these links instead and pass each one to the right profile, asking you when more than one is open.\n\n" +
                       "Windows only lets you make that choice yourself: in the Settings page that opens next, set the CLAUDE link type to CC Switcher.",
                Icon = TaskDialogIcon.Information,
                Buttons = { proceed, TaskDialogButton.Cancel },
            };
            if (TaskDialog.ShowDialog(this, page) != proceed)
                return;
        }

        try
        {
            LinkRouter.Register();
            LinkRouter.OpenWindowsSettings();
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't set up link routing:\n{ex.Message}");
        }
        RefreshLinkState(force: true);
    }

    void TurnOffLinkRouting()
    {
        try
        {
            LinkRouter.Unregister();
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't turn off link routing:\n{ex.Message}");
        }
        RefreshLinkState(force: true);
    }

    void ShowError(string message) =>
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);

    static Button MakeButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(76, 28),
        Padding = new Padding(6, 0, 6, 0),
        Margin = new Padding(0, 0, 6, 0),
    };
}
