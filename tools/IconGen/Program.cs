using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

// Draws Speak Forever's brand art from the logo geometry in assets/logo.svg and assets/logo-static.svg:
//   IconGen icon <out.ico> [preview.png]        multi-size app icon
//   IconGen wizard <outdir> <Grenze.ttf>         installer side panel and header images
// Everything is drawn large and scaled down, so small sizes stay smooth.
switch (args)
{
    case ["icon", var ico, ..]:
        WriteIcon(ico, args.Length > 2 ? args[2] : null);
        break;
    case ["wizard", var dir, var font]:
        WriteWizardArt(dir, font);
        break;
    default:
        Console.Error.WriteLine("Usage: IconGen icon <out.ico> [preview.png] | IconGen wizard <outdir> <Grenze.ttf>");
        return 1;
}
return 0;

static void WriteIcon(string path, string? preview)
{
    int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
    var images = sizes.ToDictionary(s => s, s => Render(s, s, g => DrawLogo(g, new RectangleF(0, 0, s * 8, s * 8), simple: s <= 32)));

    using (var ico = File.Create(path))
    using (var w = new BinaryWriter(ico))
    {
        var pngs = sizes.Select(s => Png(images[s])).ToList();
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);
    }
    if (preview is not null)
    {
        using var sheet = new Bitmap(sizes.Sum() + 10 * sizes.Length + 10, 276);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(245, 245, 245));
        int x = 10;
        foreach (var s in sizes) { g.DrawImage(images[s], x, 10, s, s); x += s + 10; }
        sheet.Save(preview, ImageFormat.Png);
    }
    Console.WriteLine($"Wrote {path} ({string.Join(", ", sizes)} px)");
}

// Inno Setup picks the closest size for the display's scaling, so render several.
static void WriteWizardArt(string dir, string fontPath)
{
    Directory.CreateDirectory(dir);
    using var fonts = new PrivateFontCollection();
    fonts.AddFontFile(fontPath);
    var grenze = fonts.Families[0];
    foreach (int percent in new[] { 100, 125, 150, 175, 200, 250 })
    {
        float k = percent / 100f;
        int w = (int)Math.Round(164 * k), h = (int)Math.Round(314 * k);
        using var large = Render(w, h, g => DrawSidePanel(g, w * 8, h * 8, grenze));
        large.Save(Path.Combine(dir, $"wizard-large-{percent}.png"), ImageFormat.Png);
        int s = (int)Math.Round(55 * k);
        using var small = Render(s, s, g => DrawLogo(g, new RectangleF(s * 8 * 0.06f, s * 8 * 0.06f, s * 8 * 0.88f, s * 8 * 0.88f), simple: s <= 64));
        small.Save(Path.Combine(dir, $"wizard-small-{percent}.png"), ImageFormat.Png);
    }
    Console.WriteLine($"Wrote wizard art to {dir}");
}

// The world ground: the mark, the name in Grenze, a rule, a tagline.
static void DrawSidePanel(Graphics g, float w, float h, FontFamily grenze)
{
    using (var world = new SolidBrush(Color.FromArgb(0x1A, 0x1A, 0x1E))) g.FillRectangle(world, 0, 0, w, h);

    float logo = w * 0.62f;
    DrawLogo(g, new RectangleF((w - logo) / 2, h * 0.16f, logo, logo), simple: false, tile: false);

    using var format = new StringFormat { Alignment = StringAlignment.Center };
    using var say = new SolidBrush(Color.White);
    using var title = new Font(grenze, w * 0.16f, FontStyle.Bold, GraphicsUnit.Pixel);
    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    float y = h * 0.16f + logo + h * 0.04f;
    g.DrawString("Speak", title, say, w / 2, y, format);
    g.DrawString("Forever", title, say, w / 2, y + title.Height * 0.9f, format);

    float rule = y + title.Height * 2.05f;
    using (var line = new SolidBrush(Color.FromArgb(0x2E, 0x2E, 0x36))) g.FillRectangle(line, w * 0.15f, rule, w * 0.7f, w * 0.008f);

    using var tagline = new Font(grenze, w * 0.075f, FontStyle.Regular, GraphicsUnit.Pixel);
    using var muted = new SolidBrush(Color.FromArgb(0x9D, 0x9D, 0x9D));
    g.DrawString("Your voice in WoW chat", tagline, muted, w / 2, rule + w * 0.05f, format);
}

// The mark, [∞], fitted to area (geometry from assets/logo.svg on a 128 canvas). Small sizes drop the
// black outline and thicken the strokes; tile puts it on the app icon's dark square.
static void DrawLogo(Graphics g, RectangleF area, bool simple, bool tile = true)
{
    var state = g.Save();
    g.TranslateTransform(area.X, area.Y);
    g.ScaleTransform(area.Width / 128f, area.Height / 128f);

    if (tile) using (var ground = new SolidBrush(Color.FromArgb(0x0A, 0x0A, 0x0C))) g.FillRectangle(ground, 0, 0, 128, 128);

    using var mark = new GraphicsPath();
    mark.StartFigure(); mark.AddLines([new PointF(34, 22), new PointF(18, 22), new PointF(18, 106), new PointF(34, 106)]);
    mark.StartFigure(); mark.AddLines([new PointF(94, 22), new PointF(110, 22), new PointF(110, 106), new PointF(94, 106)]);
    mark.StartFigure();
    mark.AddBezier(64, 64, 72, 48, 92, 48, 92, 64);
    mark.AddBezier(92, 64, 92, 80, 72, 80, 64, 64);
    mark.AddBezier(64, 64, 56, 48, 36, 48, 36, 64);
    mark.AddBezier(36, 64, 36, 80, 56, 80, 64, 64);
    mark.CloseFigure();

    var strokes = simple || tile ? new[] { (Color.FromArgb(0xFF, 0x80, 0x00), simple ? 15f : 11f) }
                                 : new[] { (Color.Black, 16f), (Color.FromArgb(0xFF, 0x80, 0x00), 10f) };
    foreach (var (color, width) in strokes)
    {
        using var pen = new Pen(color, width) { LineJoin = LineJoin.Miter, StartCap = LineCap.Square, EndCap = LineCap.Square };
        g.DrawPath(pen, mark);
    }
    g.Restore(state);
}

// Draws at 8x with antialiasing, then scales down to the target size.
static Bitmap Render(int width, int height, Action<Graphics> draw)
{
    const int Scale = 8;
    using var big = new Bitmap(width * Scale, height * Scale);
    using (var g = Graphics.FromImage(big))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        draw(g);
    }
    var result = new Bitmap(width, height);
    using (var g = Graphics.FromImage(result))
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(big, 0, 0, width, height);
    }
    return result;
}

static byte[] Png(Bitmap image)
{
    using var ms = new MemoryStream();
    image.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}
