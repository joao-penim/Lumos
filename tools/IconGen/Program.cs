// Renders the SVG sources in assets/ into the icon files the apps embed:
//   lumos.svg       -> lumos.ico (app/exe icon), lumos-256.png (UI logo)
//   tray-*.svg      -> tray-*.ico (notification-area icons, one per state)
// Each size is rendered from the vector source (not downscaled from a big bitmap), so small sizes stay crisp.
// ICO entries are stored as PNG, which Windows supports since Vista.
using SkiaSharp;
using Svg.Skia;

// Optional: --preview out.png renders every icon at tray sizes on dark and light backgrounds (4x, nearest-neighbour).
var previewIndex = Array.IndexOf(args, "--preview");
var previewPath = previewIndex >= 0 && previewIndex + 1 < args.Length ? args[previewIndex + 1] : null;
var positional = args.Where((a, i) => !a.StartsWith("--") && (previewIndex < 0 || i != previewIndex + 1)).ToArray();
var assets = positional.Length > 0 ? positional[0] : FindAssets();

int[] appSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
int[] traySizes = [16, 20, 24, 32, 40, 48, 64];

WriteIco("lumos.svg", "lumos.ico", appSizes);
File.WriteAllBytes(Path.Combine(assets, "lumos-256.png"), Render("lumos.svg", 256));
Console.WriteLine("wrote lumos-256.png");
foreach (var state in new[] { "on", "off", "paused" })
    WriteIco($"tray-{state}.svg", $"tray-{state}.ico", traySizes);
if (previewPath is not null) WritePreview(previewPath);

void WritePreview(string path)
{
    string[] svgs = ["tray-on.svg", "tray-off.svg", "tray-paused.svg", "lumos.svg"];
    int[] sizes = [16, 20, 24, 32, 48];
    const int scale = 4, pad = 16;
    var rowH = sizes.Max() * scale + pad * 2;
    var width = sizes.Sum(s => s * scale + pad) + pad;
    using var surface = SKSurface.Create(new SKImageInfo(width, rowH * svgs.Length * 2));
    var canvas = surface.Canvas;
    using var paint = new SKPaint();
    var y = 0;
    foreach (var bg in new[] { new SKColor(32, 32, 32), new SKColor(243, 243, 243) })
    {
        foreach (var svg in svgs)
        {
            paint.Color = bg;
            canvas.DrawRect(0, y, width, rowH, paint);
            var x = pad;
            foreach (var s in sizes)
            {
                using var img = SKImage.FromEncodedData(Render(svg, s));
                canvas.DrawImage(img, SKRect.Create(x, y + pad, s * scale, s * scale), new SKSamplingOptions(SKFilterMode.Nearest));
                x += s * scale + pad;
            }
            y += rowH;
        }
    }
    using var snap = surface.Snapshot();
    using var data = snap.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(path, data.ToArray());
    Console.WriteLine($"wrote preview {path}");
}

void WriteIco(string svgName, string icoName, int[] sizes)
{
    var images = sizes.Select(s => Render(svgName, s)).ToArray();
    using var fs = File.Create(Path.Combine(assets, icoName));
    using var w = new BinaryWriter(fs);
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        var dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
        w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write((uint)images[i].Length); w.Write((uint)offset);
        offset += images[i].Length;
    }
    foreach (var img in images) w.Write(img);
    Console.WriteLine($"wrote {icoName} ({string.Join(", ", sizes)})");
}

byte[] Render(string svgName, int size)
{
    using var svg = new SKSvg();
    var picture = svg.Load(Path.Combine(assets, svgName)) ?? throw new InvalidOperationException($"Can't load {svgName}");
    var bounds = picture.CullRect;
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(size / bounds.Width, size / bounds.Height);
    canvas.DrawPicture(picture);
    canvas.Flush();
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

static string FindAssets()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "assets");
        if (File.Exists(Path.Combine(candidate, "lumos.svg"))) return candidate;
    }
    throw new DirectoryNotFoundException("assets/lumos.svg not found; pass the assets folder as the first argument.");
}
