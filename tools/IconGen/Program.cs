using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

// Draws Voice Forever's brand art from the logo geometry in assets/logo-static.svg:
//   IconGen icon <out.ico> [preview.png]        multi-size app icon
//   IconGen wizard <outdir> <Cinzel.ttf>         installer side panel and header images
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
        Console.Error.WriteLine("Usage: IconGen icon <out.ico> [preview.png] | IconGen wizard <outdir> <Cinzel.ttf>");
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
    var cinzel = fonts.Families[0];
    foreach (int percent in new[] { 100, 125, 150, 175, 200, 250 })
    {
        float k = percent / 100f;
        int w = (int)Math.Round(164 * k), h = (int)Math.Round(314 * k);
        using var large = Render(w, h, g => DrawSidePanel(g, w * 8, h * 8, cinzel));
        large.Save(Path.Combine(dir, $"wizard-large-{percent}.png"), ImageFormat.Png);
        int s = (int)Math.Round(55 * k);
        using var small = Render(s, s, g => DrawLogo(g, new RectangleF(s * 8 * 0.06f, s * 8 * 0.06f, s * 8 * 0.88f, s * 8 * 0.88f), simple: s <= 64));
        small.Save(Path.Combine(dir, $"wizard-small-{percent}.png"), ImageFormat.Png);
    }
    Console.WriteLine($"Wrote wizard art to {dir}");
}

// Night-sky panel: the logo, the name in Cinzel, a silver rule with a stud, a tagline.
static void DrawSidePanel(Graphics g, float w, float h, FontFamily cinzel)
{
    using (var sky = new LinearGradientBrush(new RectangleF(0, 0, w, h), Color.FromArgb(0x16, 0x1C, 0x38), Color.FromArgb(0x06, 0x07, 0x0E), 90f))
        g.FillRectangle(sky, 0, 0, w, h);
    using (var glow = new GraphicsPath())
    {
        glow.AddEllipse(-w * 0.3f, h * 0.05f, w * 1.6f, h * 0.6f);
        using var halo = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(70, 0x2E, 0x3F, 0x8F), SurroundColors = [Color.FromArgb(0, 0x2E, 0x3F, 0x8F)] };
        g.FillPath(halo, glow);
    }

    float logo = w * 0.66f;
    DrawLogo(g, new RectangleF((w - logo) / 2, h * 0.17f, logo, logo), simple: false);

    using var format = new StringFormat { Alignment = StringAlignment.Center };
    using var starlight = new SolidBrush(Color.FromArgb(0xE0, 0xEC, 0xFF));
    using var title = new Font(cinzel, w * 0.125f, FontStyle.Regular, GraphicsUnit.Pixel);
    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    float y = h * 0.17f + logo + h * 0.05f;
    g.DrawString("VOICE", title, starlight, w / 2, y, format);
    g.DrawString("FOREVER", title, starlight, w / 2, y + title.Height * 0.95f, format);

    float rule = y + title.Height * 2.15f;
    using (var line = new LinearGradientBrush(new RectangleF(w * 0.12f, rule, w * 0.76f, 8), Color.FromArgb(0, 0x8A, 0x93, 0xB8), Color.FromArgb(0, 0x8A, 0x93, 0xB8), 0f))
    {
        line.InterpolationColors = new ColorBlend
        {
            Colors = [Color.FromArgb(0, 0x8A, 0x93, 0xB8), Color.FromArgb(0x8A, 0x93, 0xB8), Color.FromArgb(0, 0x8A, 0x93, 0xB8)],
            Positions = [0f, 0.5f, 1f],
        };
        g.FillRectangle(line, w * 0.12f, rule, w * 0.76f, w * 0.008f);
    }
    float stud = w * 0.04f;
    var state = g.Save();
    g.TranslateTransform(w / 2, rule + w * 0.004f);
    g.RotateTransform(45);
    using (var silver = new SolidBrush(Color.FromArgb(0x8A, 0x93, 0xB8))) g.FillRectangle(silver, -stud / 2, -stud / 2, stud, stud);
    g.Restore(state);

    using var tagline = new Font(cinzel, w * 0.062f, FontStyle.Regular, GraphicsUnit.Pixel);
    using var silverText = new SolidBrush(Color.FromArgb(0x8A, 0x93, 0xB8));
    g.DrawString("Speak, and it types", tagline, silverText, w / 2, rule + w * 0.07f, format);
}

// The logo, fitted to area; simple drops the inner ring and thickens the loop for tiny sizes.
static void DrawLogo(Graphics g, RectangleF area, bool simple)
{
    var state = g.Save();
    g.TranslateTransform(area.X, area.Y);
    g.ScaleTransform(area.Width / 256f, area.Height / 256f);

    using var navy = new SolidBrush(Color.FromArgb(0x12, 0x16, 0x2A));
    g.FillEllipse(navy, 8, 8, 240, 240);
    using var silver = new Pen(Color.FromArgb(0x8A, 0x93, 0xB8), simple ? 14 : 8);
    float ring = simple ? 119 : 116;
    g.DrawEllipse(silver, 128 - ring, 128 - ring, ring * 2, ring * 2);
    if (!simple)
    {
        using var indigo = new Pen(Color.FromArgb(0x3A, 0x42, 0x70), 3);
        g.DrawEllipse(indigo, 24, 24, 208, 208);
    }

    using var loop = new GraphicsPath();
    loop.AddBezier(128, 128, 152, 80, 212, 80, 212, 128);
    loop.AddBezier(212, 128, 212, 176, 152, 176, 128, 128);
    loop.AddBezier(128, 128, 104, 80, 44, 80, 44, 128);
    loop.AddBezier(44, 128, 44, 176, 104, 176, 128, 128);
    float k = simple ? 1.5f : 1f;
    foreach (var (color, width) in new[] { (Color.FromArgb(0x2E, 0x3F, 0x8F), 24f), (Color.FromArgb(0x7A, 0xA7, 0xFF), 15f), (Color.FromArgb(0xE0, 0xEC, 0xFF), 5f) })
    {
        using var pen = new Pen(color, width * k) { LineJoin = LineJoin.Round };
        g.DrawPath(pen, loop);
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
