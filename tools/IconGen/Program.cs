using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Renders the Rain logo (designed on a 256x256 grid) to PNGs and a multi-size .ico.
// Usage: dotnet run --project tools/IconGen -- <output folder>
var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
var images = sizes.Select(size => (size, Render(size))).ToList();
foreach (var size in new[] { 64, 256, 512 })
    File.WriteAllBytes(Path.Combine(outDir, $"logo-{size}.png"), Png(Render(size)));
WriteIco(Path.Combine(outDir, "Rain.ico"), images);
Console.WriteLine($"wrote icon + pngs to {Path.GetFullPath(outDir)}");

static BitmapSource Render(int size)
{
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
        Draw(dc, detailed: size >= 40);
        dc.Pop();
    }
    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    return new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); // icons want straight alpha
}

static byte[] Png(BitmapSource image)
{
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(image));
    using var ms = new MemoryStream();
    encoder.Save(ms);
    return ms.ToArray();
}

/// <summary>Classic 32-bit DIB icon entry (readable by every Windows API, unlike PNG entries below 256px).</summary>
static byte[] Dib(BitmapSource image)
{
    int size = image.PixelWidth, stride = size * 4;
    var pixels = new byte[stride * size];
    image.CopyPixels(pixels, stride, 0);

    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write(40); w.Write(size); w.Write(size * 2);           // BITMAPINFOHEADER; height covers XOR + AND masks
    w.Write((ushort)1); w.Write((ushort)32);
    w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
    for (var y = size - 1; y >= 0; y--) w.Write(pixels, y * stride, stride); // bottom-up
    var maskStride = (size + 31) / 32 * 4;
    w.Write(new byte[maskStride * size]);                    // AND mask unused: alpha does the work
    return ms.ToArray();
}

static void Draw(DrawingContext dc, bool detailed)
{
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Brush Frozen(Brush b) { b.Freeze(); return b; }

    // Tile: dark rounded square with a soft top-down gradient and a faint rim.
    var tile = new Rect(8, 8, 240, 240);
    var tileFill = Frozen(new LinearGradientBrush(C("#262D3D"), C("#0C0E13"), new Point(0.3, 0), new Point(0.7, 1)));
    var rim = new Pen(Frozen(new LinearGradientBrush(C("#33FFFFFF"), C("#08FFFFFF"), 90)), 2);
    rim.Freeze();
    dc.DrawRoundedRectangle(tileFill, rim, tile, 56, 56);

    // Soft blue glow behind the drop.
    var glow = Frozen(new RadialGradientBrush(C("#447AA2F7"), C("#007AA2F7")) { Center = new Point(0.5, 0.55), GradientOrigin = new Point(0.5, 0.55), RadiusX = 0.42, RadiusY = 0.42 });
    dc.DrawRectangle(glow, null, tile);

    if (detailed)
    {
        // Falling rain streaks.
        var streak = new Pen(Frozen(new LinearGradientBrush(C("#00FFFFFF"), C("#40FFFFFF"), 90)), 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        streak.Freeze();
        foreach (var (x, y, len) in new[] { (58.0, 46.0, 44.0), (196.0, 64.0, 36.0), (40.0, 150.0, 26.0), (186.0, 136.0, 30.0) })
            dc.DrawLine(streak, new Point(x + len * 0.25, y), new Point(x, y + len));

        // Ripples where the drop lands.
        var outer = new Pen(new SolidColorBrush(C("#2EFFFFFF")), 3); outer.Freeze();
        var inner = new Pen(new SolidColorBrush(C("#59FFFFFF")), 3); inner.Freeze();
        dc.DrawEllipse(null, outer, new Point(128, 210), 62, 11);
        dc.DrawEllipse(null, inner, new Point(128, 210), 34, 6);
    }

    // The drop: pointed top, round bottom.
    var drop = Geometry.Parse("M128,38 C128,38 74,104 74,140 A54,54 0 0 0 182,140 C182,104 128,38 128,38 Z");
    var dropFill = Frozen(new LinearGradientBrush(
        [new GradientStop(C("#F4F6FA"), 0), new GradientStop(C("#C9D6F2"), 0.45), new GradientStop(C("#7AA2F7"), 1)],
        new Point(0.35, 0), new Point(0.65, 1)));
    var dropEdge = new Pen(new SolidColorBrush(C("#66FFFFFF")), 1.5); dropEdge.Freeze();
    dc.DrawGeometry(dropFill, dropEdge, drop);

    // Glassy highlight on the left side of the drop.
    var shine = Geometry.Parse("M100,128 C100,112 110,96 118,86 C112,100 108,114 108,130 C108,140 112,150 118,156 C106,152 100,140 100,128 Z");
    dc.DrawGeometry(Frozen(new SolidColorBrush(C("#B3FFFFFF"))), null, shine);
}

static void WriteIco(string path, List<(int Size, BitmapSource Image)> sources)
{
    var images = sources.Select(s => (s.Size, Data: s.Size >= 256 ? Png(s.Image) : Dib(s.Image))).ToList();
    using var w = new BinaryWriter(File.Create(path));
    w.Write((ushort)0);            // reserved
    w.Write((ushort)1);            // type: icon
    w.Write((ushort)images.Count);
    var offset = 6 + 16 * images.Count;
    foreach (var (size, data) in images)
    {
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);          // palette
        w.Write((byte)0);          // reserved
        w.Write((ushort)1);        // planes
        w.Write((ushort)32);       // bpp
        w.Write(data.Length);
        w.Write(offset);
        offset += data.Length;
    }
    foreach (var (_, data) in images) w.Write(data);
}
