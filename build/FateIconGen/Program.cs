using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FateTakesYouHome.Branding;

namespace FateTakesYouHome.IconGen;

/// <summary>
/// Renders the brand mark to the asset files the application and installer consume.
/// </summary>
/// <remarks>
/// Run this after changing <see cref="FateMark"/>, then commit the results. The generated files are
/// committed rather than produced during the build so that a clean clone builds without needing to
/// run a tool first — and so a change to the mark shows up as a reviewable diff.
/// </remarks>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string root = args.Length > 0
                ? args[0]
                : LocateRepositoryRoot();

            string icons = Path.Combine(root, "src", "FateTakesYouHome", "Assets", "Icons");
            Directory.CreateDirectory(icons);

            string icoPath = Path.Combine(icons, "app.ico");
            FateMark.WriteIcoFile(icoPath);
            Report(icoPath);

            // Plated marks for the installer, the about page and the repository README.
            foreach (int size in new[] { 64, 128, 256, 512 })
            {
                string png = Path.Combine(icons, $"mark-{size}.png");
                FateMark.WritePngFile(png, size);
                Report(png);
            }

            // Unplated glyph for use on surfaces that already supply their own background.
            string glyph = Path.Combine(icons, "glyph-256.png");
            FateMark.WritePngFile(glyph, 256, withPlate: false);
            Report(glyph);

            // Magnified proof sheet. Small sizes are where an icon actually fails, and they are
            // impossible to judge at their real size, so each is blown up with nearest-neighbour
            // sampling to show the exact pixels the shell will draw.
            string proof = Path.Combine(icons, "proof-small-sizes.png");
            WriteProofSheet(
                proof,
                [
                    // Top row: the tray icon, which is never plated.
                    (16, false), (20, false), (24, false), (32, false),

                    // Bottom row: the application icon, which always is.
                    (16, true), (20, true), (24, true), (32, true),
                ],
                perRow: 4,
                magnification: 8);
            Report(proof);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Icon generation failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Composites the given renders in a grid, magnified, with no smoothing.</summary>
    private static void WriteProofSheet(
        string path, (int Size, bool Plated)[] cells, int perRow, int magnification)
    {
        const int gap = 12;

        int cellExtent = cells.Max(c => c.Size) * magnification;
        int rows = (int)Math.Ceiling(cells.Length / (double)perRow);

        int totalWidth = (perRow * (cellExtent + gap)) + gap;
        int totalHeight = (rows * (cellExtent + gap)) + gap;

        var visual = new DrawingVisual();

        // Nearest-neighbour on the visual: the point is to see individual pixels, not a preview.
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);

        using (DrawingContext dc = visual.RenderOpen())
        {
            // Mid grey shows both the light and the dark edges of the mark honestly.
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                null,
                new Rect(0, 0, totalWidth, totalHeight));

            for (int i = 0; i < cells.Length; i++)
            {
                (int size, bool plated) = cells[i];

                double x = gap + ((i % perRow) * (cellExtent + gap));
                double y = gap + ((i / perRow) * (cellExtent + gap));
                double drawn = size * magnification;

                // Bottom-align within the cell so the different sizes sit on a common baseline.
                dc.DrawImage(
                    FateMark.Render(size, plated),
                    new Rect(x, y + (cellExtent - drawn), drawn, drawn));
            }
        }

        var render = new RenderTargetBitmap(totalWidth, totalHeight, 96, 96, PixelFormats.Pbgra32);
        render.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(render));

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(file);
    }

    /// <summary>Walks up from the executable until the solution file appears.</summary>
    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FateTakesYouHome.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not find the repository root. Pass it as the first argument.");
    }

    private static void Report(string path)
    {
        var info = new FileInfo(path);
        Console.WriteLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "  wrote {0,-22} {1,7:N0} bytes",
                Path.GetFileName(path),
                info.Length));
    }
}
