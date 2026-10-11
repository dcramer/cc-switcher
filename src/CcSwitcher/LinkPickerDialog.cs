namespace CcSwitcher;

/// <summary>Asks which profile should receive a claude:// link.</summary>
sealed class LinkPickerDialog : Form
{
    public Profile? Selected { get; private set; }

    public LinkPickerDialog(Settings settings, string linkKind, IReadOnlyCollection<Profile> running)
    {
        SuspendLayout();
        Text = "CC Switcher";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        }
        catch (Exception)
        {
            // Cosmetic only.
        }

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };

        layout.Controls.Add(new Label
        {
            Text = $"Open this {linkKind} in which profile?",
            Font = new Font(Font.FontFamily, Font.Size + 2, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 4),
        });
        layout.Controls.Add(new Label
        {
            Text = "If you're signing in, pick the profile whose window you started from.",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 12),
        });

        var iconSize = 24 * DeviceDpi / 96;
        var preferred = running.FirstOrDefault(p => p.Id == settings.LastProfileId)
            ?? running.FirstOrDefault()
            ?? settings.Profiles.FirstOrDefault(p => p.Id == settings.LastProfileId);

        foreach (var profile in settings.Profiles)
        {
            var button = new Button
            {
                Text = "  " + profile.Name + (running.Contains(profile) ? "  (running)" : ""),
                Image = IconFactory.Render(iconSize, profile),
                ImageAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleLeft,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Size = new Size(340, 44),
                Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(3, 0, 3, 6),
            };
            button.Click += (_, _) =>
            {
                Selected = profile;
                DialogResult = DialogResult.OK;
            };
            layout.Controls.Add(button);
            if (profile == preferred)
                Shown += (_, _) => button.Select();
        }

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size = new Size(88, 28),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 8, 3, 0),
        };
        layout.Controls.Add(cancel);
        CancelButton = cancel;

        Controls.Add(layout);

        // Sizes above are in 96-DPI units. Like designer-generated code, scale once at the end:
        // WinForms only applies DPI scaling to what's inside a SuspendLayout/ResumeLayout pair.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }
}
