using System.Drawing.Drawing2D;

namespace CcSwitcher;

/// <summary>
/// The profile list: an icon, a name and a status on each row. Drawn here rather than by a
/// ListView, whose modern theme rules a line down every column edge that can't be turned off.
/// </summary>
sealed class ProfileList : ListBox
{
    const int RowHeight = 40;
    // One of the sizes icons are stored at, at the usual display scales.
    const int IconSize = 32;

    static readonly Color Accent = ColorTranslator.FromHtml(IconFactory.Palette[0].Hex);

    readonly Dictionary<Profile, Bitmap> _icons = [];
    readonly Dictionary<Profile, (string Text, bool Running)> _status = [];

    public ProfileList()
    {
        BorderStyle = BorderStyle.None;
        DrawMode = DrawMode.OwnerDrawFixed;
        IntegralHeight = false;
        ItemHeight = Scaled(RowHeight);
        // What assistive technology reads for a row.
        DisplayMember = nameof(Profile.Name);
    }

    public Profile? Selected => SelectedItem as Profile;

    /// <summary>A row was double-clicked.</summary>
    public event Action? RowActivated;

    /// <summary>The list inside a hairline frame; the border a list box draws for itself is heavy, glaringly so in dark mode.</summary>
    public Control Framed(Padding margin)
    {
        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = margin,
            Padding = new Padding(1),
            BackColor = SystemInformation.HighContrast ? SystemColors.WindowFrame
                : Application.IsDarkModeEnabled ? Color.FromArgb(0x4D, 0x4D, 0x4D)
                : Color.FromArgb(0xD1, 0xD1, 0xD1),
        };
        Dock = DockStyle.Fill;
        frame.Controls.Add(this);
        return frame;
    }

    public void SetProfiles(Settings settings, string? selectId)
    {
        BeginUpdate();
        Items.Clear();
        DisposeIcons();
        _status.Clear();
        foreach (var profile in settings.Profiles)
        {
            _icons[profile] = IconFactory.Render(settings, Scaled(IconSize), profile);
            Items.Add(profile);
        }
        SelectedItem = settings.Profiles.FirstOrDefault(p => p.Id == selectId) ?? settings.Profiles.FirstOrDefault();
        EndUpdate();
        RevealSelection();
    }

    /// <summary>
    /// Scrolls no further than it takes to show the selected row. A list box does this itself on
    /// selection, but for the size it has then, which before the window is laid out is not its real one.
    /// </summary>
    public void RevealSelection() =>
        TopIndex = Math.Max(0, SelectedIndex - Math.Max(1, ClientSize.Height / ItemHeight) + 1);

    public void SetStatus(Profile profile, string text, bool running)
    {
        if (_status.GetValueOrDefault(profile, ("", false)) == (text, running))
            return;
        _status[profile] = (text, running);
        var index = Items.IndexOf(profile);
        if (index >= 0)
            Invalidate(GetItemRectangle(index));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposeIcons();
        base.Dispose(disposing);
    }

    // A right-click selects the row first, so the menu that follows is about the row under the pointer.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right && RowAt(e.Location) is { } index)
            SelectedIndex = index;
        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && RowAt(e.Location) is not null)
            RowActivated?.Invoke();
    }

    // IndexFromPoint alone answers with the last row for the empty space beneath it.
    int? RowAt(Point location) =>
        IndexFromPoint(location) is >= 0 and var index && GetItemRectangle(index).Contains(location) ? index : null;

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count)
            return;

        var profile = (Profile)Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        var highContrast = SystemInformation.HighContrast;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var background = new SolidBrush(BackColor))
            g.FillRectangle(background, e.Bounds);

        var row = Rectangle.Inflate(e.Bounds, -Scaled(3), -Scaled(2));
        if (selected)
        {
            var fill = highContrast ? SystemColors.Highlight
                : Application.IsDarkModeEnabled ? Color.FromArgb(0x4D, 0x4D, 0x4D)
                : Color.FromArgb(0xEC, 0xEC, 0xEC);
            using var brush = new SolidBrush(fill);
            using var shape = Rounded(row, Scaled(5));
            g.FillPath(brush, shape);

            if (!highContrast)
            {
                // The marker Windows 11 puts beside the selected row of a list.
                var marker = new Rectangle(row.X, row.Y + (row.Height - Scaled(16)) / 2, Scaled(3), Scaled(16));
                using var accent = new SolidBrush(Accent);
                using var pill = Rounded(marker, marker.Width);
                g.FillPath(accent, pill);
            }
        }

        if (_icons.TryGetValue(profile, out var icon))
            g.DrawImageUnscaled(icon, row.X + Scaled(10), row.Y + (row.Height - icon.Height) / 2);

        var (status, running) = _status.GetValueOrDefault(profile, ("", false));
        var textColor = selected && highContrast ? SystemColors.HighlightText : ForeColor;
        var statusColor = selected && highContrast ? SystemColors.HighlightText
            : !running ? SystemColors.GrayText
            : Application.IsDarkModeEnabled ? Color.FromArgb(0x6F, 0xCF, 0x8E)
            : Color.FromArgb(0x16, 0x7C, 0x3D);

        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var statusWidth = status.Length == 0 ? 0 : TextRenderer.MeasureText(g, status, Font, row.Size, flags).Width;
        var statusArea = new Rectangle(row.Right - Scaled(12) - statusWidth, row.Y, statusWidth, row.Height);
        var nameLeft = row.X + Scaled(10 + IconSize + 10);
        var nameArea = Rectangle.FromLTRB(nameLeft, row.Y, Math.Max(nameLeft, statusArea.X - Scaled(8)), row.Bottom);

        TextRenderer.DrawText(g, profile.Name, Font, nameArea, textColor, flags | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, status, Font, statusArea, statusColor, flags);

        if ((e.State & DrawItemState.Focus) != 0 && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, row);
    }

    void DisposeIcons()
    {
        foreach (var icon in _icons.Values)
            icon.Dispose();
        _icons.Clear();
    }

    int Scaled(int size) => size * DeviceDpi / 96;

    static GraphicsPath Rounded(Rectangle rect, int diameter)
    {
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
