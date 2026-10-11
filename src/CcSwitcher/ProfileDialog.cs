namespace CcSwitcher;

/// <summary>Asks for a profile's name and color; the profile's folders are managed for the user.</summary>
sealed class ProfileDialog : Form
{
    public string ProfileName => _name.Text.Trim();
    public string ProfileColor { get; private set; }
    public bool SeparateCodeConfig => _separateConfig.Checked;

    readonly Settings _settings;
    readonly Profile? _existing;
    readonly TextBox _name = new() { Dock = DockStyle.Fill };
    readonly FlowLayoutPanel _colors = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
    readonly CheckBox _separateConfig = new() { Text = "Also separate Claude Code settings and history", AutoSize = true };

    public ProfileDialog(Settings settings, Profile? existing)
    {
        _settings = settings;
        _existing = existing;
        ProfileColor = existing?.Color ?? NextUnusedColor(settings);

        SuspendLayout();
        Text = existing is null ? "New profile" : "Edit profile";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 10);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowOnly;
        Padding = new Padding(12);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(FieldLabel("Name"), 0, 0);
        layout.Controls.Add(_name, 1, 0);
        layout.Controls.Add(FieldLabel("Color"), 0, 1);
        layout.Controls.Add(_colors, 1, 1);

        _separateConfig.Margin = new Padding(3, 12, 3, 3);
        layout.Controls.Add(_separateConfig, 1, 2);
        layout.Controls.Add(new Label
        {
            Text = "Each profile always has its own account. This also gives it its own Claude Code config folder instead of sharing the .claude folder in your user folder.",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Margin = new Padding(20, 0, 3, 3),
        }, 1, 3);

        var ok = new Button { Text = "OK", Size = new Size(88, 28) };
        var cancel = new Button { Text = "Cancel", Size = new Size(88, 28), DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        // Docked controls stack in reverse order of addition.
        Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;

        BuildColorSwatches();
        _name.Text = existing?.Name ?? "";
        _separateConfig.Checked = existing?.SeparateCodeConfig ?? false;
        ok.Click += (_, _) => Accept();

        // Sizes above are in 96-DPI units. Like designer-generated code, scale once at the end:
        // WinForms only applies DPI scaling to what's inside a SuspendLayout/ResumeLayout pair.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    void BuildColorSwatches()
    {
        foreach (var (name, hex) in IconFactory.Palette)
        {
            var color = IconFactory.ParseColor(hex);
            var swatch = new RadioButton
            {
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                Size = new Size(28, 28),
                Margin = new Padding(3, 3, 5, 3),
                Checked = hex.Equals(ProfileColor, StringComparison.OrdinalIgnoreCase),
                AccessibleName = name,
            };
            swatch.FlatAppearance.CheckedBackColor = color;
            swatch.FlatAppearance.MouseOverBackColor = color;
            swatch.FlatAppearance.MouseDownBackColor = color;
            StyleSwatch(swatch);
            swatch.CheckedChanged += (_, _) =>
            {
                StyleSwatch(swatch);
                if (swatch.Checked)
                    ProfileColor = hex;
            };
            _colors.Controls.Add(swatch);
        }
    }

    static void StyleSwatch(RadioButton swatch)
    {
        swatch.FlatAppearance.BorderSize = swatch.Checked ? 3 : 1;
        swatch.FlatAppearance.BorderColor = swatch.Checked ? SystemColors.ControlText : SystemColors.ControlDark;
    }

    void Accept()
    {
        string? problem = null;
        if (ProfileName.Length == 0)
            problem = "Give the profile a name.";
        else if (_settings.Profiles.Any(p => p != _existing && p.Name.Equals(ProfileName, StringComparison.OrdinalIgnoreCase)))
            problem = $"There's already a profile named \"{ProfileName}\".";

        if (problem is not null)
        {
            MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _name.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
    }

    static string NextUnusedColor(Settings settings)
    {
        var used = settings.Profiles.Select(p => p.Color).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var free = IconFactory.Palette.FirstOrDefault(c => !used.Contains(c.Hex));
        return free.Hex ?? IconFactory.Palette[0].Hex;
    }

    static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 6, 12, 3),
    };
}
