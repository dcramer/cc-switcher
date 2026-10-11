using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CcSwitcher;

/// <summary>Draws the colored initial badge used for each profile's list icon and shortcut icon.</summary>
static class IconFactory
{
    public static readonly (string Name, string Hex)[] Palette =
    [
        ("Clay", "#D97757"),
        ("Blue", "#3B82F6"),
        ("Green", "#10B981"),
        ("Purple", "#8B5CF6"),
        ("Amber", "#F59E0B"),
        ("Rose", "#F43F5E"),
        ("Teal", "#14B8A6"),
        ("Slate", "#64748B"),
    ];

    public static Color ParseColor(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
                return ColorTranslator.FromHtml(hex);
        }
        catch (Exception)
        {
            // Hand-edited settings; fall through to the default.
        }
        return ColorTranslator.FromHtml(Palette[0].Hex);
    }

    public static Bitmap Render(int size, Profile profile) =>
        Render(size, ParseColor(profile.Color), Initial(profile.Name));

    public static Bitmap Render(int size, Color color, string glyph)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        var inset = size * 0.04f;
        var bounds = new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset);
        using (var badge = RoundedRect(bounds, size * 0.44f))
        using (var fill = new SolidBrush(color))
            g.FillPath(fill, badge);

        // Outline the glyph so it can be centered on its actual ink rather than its line box.
        using var text = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");
        text.AddString(glyph, family, (int)FontStyle.Bold, size * 0.62f, PointF.Empty, StringFormat.GenericTypographic);
        var ink = text.GetBounds();
        using var center = new Matrix();
        center.Translate(size / 2f - (ink.X + ink.Width / 2f), size / 2f - (ink.Y + ink.Height / 2f));
        text.Transform(center);
        g.FillPath(Brushes.White, text);

        return bitmap;
    }

    /// <summary>Writes a multi-size .ico (PNG-compressed entries) for use as a shortcut icon.</summary>
    public static void WriteIco(string path, Profile profile)
    {
        int[] sizes = [16, 24, 32, 48, 64, 256];
        var images = sizes.Select(size =>
        {
            using var bitmap = Render(size, profile);
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            return png.ToArray();
        }).ToArray();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)sizes.Length);

        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            var dimension = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            writer.Write(dimension);
            writer.Write(dimension);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (var image in images)
            writer.Write(image);
    }

    static string Initial(string name)
    {
        var first = name.FirstOrDefault(char.IsLetterOrDigit);
        return first == default ? "C" : char.ToUpperInvariant(first).ToString();
    }

    static GraphicsPath RoundedRect(RectangleF rect, float diameter)
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
