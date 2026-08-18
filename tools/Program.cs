using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace IconBuilder;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: IconBuilder <source.svg> <output-directory>");
            return 1;
        }

        var document = XDocument.Load(args[0]);
        var pathData = document.Descendants().First(element => element.Name.LocalName == "path").Attribute("d")?.Value
            ?? throw new InvalidOperationException("SVG 中没有 path 数据。");
        var geometry = Geometry.Parse(pathData);
        var outputDirectory = args[1];
        Directory.CreateDirectory(outputDirectory);

        var blueFrames = CreateFrames(geometry, Color.FromRgb(0x4D, 0x6B, 0xFE));
        var whiteFrames = CreateFrames(geometry, Colors.White);
        WritePng(Path.Combine(outputDirectory, "deepseek-whale-blue.png"), blueFrames[^1]);
        WriteIco(Path.Combine(outputDirectory, "dsh-launcher.ico"), blueFrames);
        WriteIco(Path.Combine(outputDirectory, "tray-blue.ico"), blueFrames);
        WriteIco(Path.Combine(outputDirectory, "tray-white.ico"), whiteFrames);
        Console.WriteLine($"Generated icons in {outputDirectory}");
        return 0;
    }

    private static List<BitmapSource> CreateFrames(Geometry geometry, Color color)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
        var frames = new List<BitmapSource>();
        foreach (var size in sizes)
        {
            var scale = (size - Math.Max(2, size * 0.08)) / 50d;
            var offset = (size - 50 * scale) / 2d;
            var frameGeometry = geometry.Clone();
            frameGeometry.Transform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(scale, scale),
                    new TranslateTransform(offset, offset)
                }
            };
            var drawing = new GeometryDrawing(new SolidColorBrush(color), null, frameGeometry);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) context.DrawDrawing(drawing);

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            frames.Add(bitmap);
        }
        return frames;
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void WritePng(string path, BitmapSource bitmap) => File.WriteAllBytes(path, EncodePng(bitmap));

    private static void WriteIco(string path, IReadOnlyList<BitmapSource> frames)
    {
        var images = frames.Select(EncodePng).ToArray();
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Length);

        var offset = 6 + images.Length * 16;
        for (var index = 0; index < images.Length; index++)
        {
            var size = frames[index].PixelWidth;
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[index].Length);
            writer.Write(offset);
            offset += images[index].Length;
        }

        foreach (var image in images) writer.Write(image);
    }
}
