using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CcSwitcher;

/// <summary>
/// Draws each profile's icon: Claude's own icon tinted to the profile's color, or a colored
/// initial badge when Claude's icon can't be read.
/// </summary>
static class IconFactory
{
    // Muted to sit beside Claude's clay, which stays first: it is the color Claude's own icon is drawn in.
    public static readonly (string Name, string Hex)[] Palette =
    [
        ("Clay", "#D97757"),
        ("Blue", "#6A9BCC"),
        ("Green", "#7A9B63"),
        ("Purple", "#9A84D0"),
        ("Amber", "#D9A441"),
        ("Rose", "#D4698C"),
        ("Teal", "#4FA8A0"),
        ("Slate", "#7D8794"),
    ];

    // The brighter palette profiles were first offered, each mapped to the color that replaced it.
    static readonly Dictionary<string, string> Retired = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#3B82F6"] = "#6A9BCC",
        ["#10B981"] = "#7A9B63",
        ["#8B5CF6"] = "#9A84D0",
        ["#F59E0B"] = "#D9A441",
        ["#F43F5E"] = "#D4698C",
        ["#14B8A6"] = "#4FA8A0",
        ["#64748B"] = "#7D8794",
    };

    /// <summary>CC Switcher's own icon in every size it comes in, so each use gets the one drawn for it.</summary>
    public static Icon App { get; } = new(typeof(IconFactory).Assembly.GetManifestResourceStream("app.ico")!);

    /// <summary>The color Claude's own icon is drawn in; a profile in this color keeps the icon as it is.</summary>
    static readonly Color StockColor = ColorTranslator.FromHtml(Palette[0].Hex);

    // The sizes icons are stored and drawn at. Anything else is scaled from the next one up.
    static readonly int[] IconSizes = [16, 24, 32, 48, 64, 256];

    static readonly Dictionary<int, Bitmap> StockCache = [];

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
        return StockColor;
    }

    /// <summary>A saved color as the palette has it today.</summary>
    public static string Current(string hex) => Retired.GetValueOrDefault(hex, hex);

    public static bool IsStockColor(Profile profile) => ParseColor(profile.Color).ToArgb() == StockColor.ToArgb();

    public static Bitmap Render(Settings settings, int size, Profile profile)
    {
        var color = ParseColor(profile.Color);
        if (Stock(settings, size) is not { } stock)
            return Render(size, color, Initial(profile.Name));

        if (color.ToArgb() != StockColor.ToArgb())
            Tint(stock, color);
        return stock;
    }

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

    /// <summary>Writes a multi-size .ico (PNG-compressed entries) for a profile's shortcuts and windows.</summary>
    public static void WriteIco(Settings settings, string path, Profile profile)
    {
        var images = IconSizes.Select(size =>
        {
            using var bitmap = Render(settings, size, profile);
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            return png.ToArray();
        }).ToArray();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)IconSizes.Length);

        var offset = 6 + 16 * IconSizes.Length;
        for (var i = 0; i < IconSizes.Length; i++)
        {
            var dimension = (byte)(IconSizes[i] >= 256 ? 0 : IconSizes[i]);
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

    /// <summary>The first icon in an executable or .ico at the given size, or null if it has none we can read.</summary>
    public static Icon? Extract(string file, int size)
    {
        var handles = new IntPtr[1];
        try
        {
            if (PrivateExtractIcons(file, 0, size, size, handles, null, 1, 0) != 1 || handles[0] == IntPtr.Zero)
                return null;
            // Clone to get an icon that owns its handle; the extracted one is ours to destroy.
            using var borrowed = Icon.FromHandle(handles[0]);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            if (handles[0] != IntPtr.Zero)
                DestroyIcon(handles[0]);
        }
    }

    /// <summary>A copy of Claude's own icon, or null when its executable can't be found or read.</summary>
    static Bitmap? Stock(Settings settings, int size)
    {
        var stored = IconSizes.FirstOrDefault(s => s >= size, IconSizes[^1]);
        if (!StockCache.TryGetValue(stored, out var source))
        {
            using var icon = ClaudeApp.IconSources(settings).Select(file => Extract(file, stored)).FirstOrDefault(i => i is not null);
            if (icon is null)
                return null;
            StockCache[stored] = source = icon.ToBitmap();
        }

        var copy = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(copy);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, size, size));
        return copy;
    }

    // Recolors in HSV: every pixel takes the target's hue, and its saturation and brightness
    // move by the same ratio that turns Claude's clay into the target. The clay plate becomes
    // the profile's color with its shading intact, while the near-white spark barely changes.
    static void Tint(Bitmap bitmap, Color target)
    {
        var (_, stockS, stockV) = ToHsv(StockColor);
        var (hue, targetS, targetV) = ToHsv(target);

        var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var pixels = new byte[data.Stride * data.Height];
        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 0)
                continue;

            var (_, s, v) = ToHsv(Color.FromArgb(pixels[i + 2], pixels[i + 1], pixels[i]));
            // How much of this pixel is the plate rather than the spark.
            var plate = Math.Min(1, s / stockS);
            var tinted = FromHsv(hue, Math.Min(1, s * targetS / stockS), Math.Min(1, v * (1 + (targetV / stockV - 1) * plate)));
            pixels[i] = tinted.B;
            pixels[i + 1] = tinted.G;
            pixels[i + 2] = tinted.R;
        }

        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);
    }

    static (double H, double S, double V) ToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - Math.Min(r, Math.Min(g, b));

        double hue = 0;
        if (delta > 0)
        {
            if (max == r)
                hue = ((g - b) / delta + 6) % 6;
            else if (max == g)
                hue = (b - r) / delta + 2;
            else
                hue = (r - g) / delta + 4;
        }
        return (hue * 60, max == 0 ? 0 : delta / max, max);
    }

    static Color FromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var (r, g, b) = ((int)(hue / 60) % 6) switch
        {
            0 => (chroma, x, 0d),
            1 => (x, chroma, 0d),
            2 => (0d, chroma, x),
            3 => (0d, x, chroma),
            4 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };
        var lift = value - chroma;
        return Color.FromArgb(Channel(r + lift), Channel(g + lift), Channel(b + lift));

        static int Channel(double c) => (int)Math.Round(c * 255);
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint PrivateExtractIcons(string file, int index, int width, int height, IntPtr[] icons, uint[]? ids, uint count, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);
}
