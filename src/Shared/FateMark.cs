// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FateTakesYouHome.Branding;

/// <summary>
/// The "Fate Takes You Home" mark, drawn from vector geometry.
/// </summary>
/// <remarks>
/// <para>
/// The mark is a gabled house whose doorway is an arch — a gate you pass through to get home. It
/// follows the naming pattern the brand already uses for the family (<c>Fate of Evrima</c>,
/// <c>Fated Updates</c>), and stays legible at 16 pixels, which rules out anything finer.
/// </para>
/// <para>
/// This file is linked into both the application and the icon generator so the runtime tray icon
/// and the compiled <c>.ico</c> can never drift apart. Rendering at runtime rather than shipping
/// bitmaps means the tray icon is crisp at any DPI, including the fractional scale factors that
/// leave pre-rendered icons looking soft.
/// </para>
/// </remarks>
public static class FateMark
{
    /// <summary>The design grid the geometry is authored on. Everything scales from this.</summary>
    public const double DesignSize = 32d;

    /// <summary>
    /// The ink bounds of the glyph on the design grid: x 3.2–28.8, y 6.5–29.3.
    /// </summary>
    /// <remarks>
    /// Hard-coded rather than measured. <see cref="Geometry.Bounds"/> on a path containing arcs
    /// returns the control-point hull, not the true extent, so measuring would leave the unplated
    /// mark visibly off-centre.
    /// </remarks>
    private static readonly Rect InkBounds = new(3.2, 6.5, 25.6, 22.8);

    /// <summary>Margin left around the glyph when it is drawn without a plate, in design units.</summary>
    private const double UnplatedMargin = 1.0;

    // The brand's own values. This file and ThemeDefaults are the only places they are written.
    private const string NavyPlateTop = "#101736";
    private const string NavyPlateBottom = "#070B1A";
    private const string GoldHighlight = "#FFE9A8";
    private const string GoldPrimary = "#D4AF37";
    private const string GoldDeep = "#B8902B";
    private const string GoldBright = "#F2C94C";

    /// <summary>
    /// The gateway arch, on the 32-unit design grid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A round-headed arch standing on two piers, drawn as a ring so the opening is a true void.
    /// The deliberate choice here is <em>not</em> to draw a house: a gabled roof over a door is the
    /// single most common icon on the internet, and at 16 pixels it collapses into an arrow. An
    /// arch is architecture rather than pictograph, it keeps its silhouette when it shrinks, and it
    /// says "the way in" — which is the whole idea of the product.
    /// </para>
    /// <para>
    /// The ring is one closed path traced outer-arc-then-inner-arc, rather than two shapes with an
    /// even-odd hole. That keeps the join between pier and arch a single continuous contour, so it
    /// stays clean at every size instead of showing a seam where two subpaths meet.
    /// </para>
    /// </remarks>
    public static Geometry BuildMarkGeometry()
    {
        // Outer contour: up the left pier, over the crown, down the right pier. Then the inner
        // contour returns in the opposite sweep direction, cutting the opening.
        const string ring =
            "M5,26.5 L5,17.5 A11,11 0 0 1 27,17.5 L27,26.5 L22,26.5 L22,17.5 "
            + "A6,6 0 0 0 10,17.5 L10,26.5 Z";

        var geometry = Geometry.Parse(ring);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// The threshold the arch stands on.
    /// </summary>
    /// <remarks>
    /// Separated from the piers by a hairline gap. That gap is what makes the mark read as
    /// engraved rather than stamped, and it closes the bottom of the opening so the arch has
    /// somewhere to stand instead of floating.
    /// </remarks>
    public static Geometry BuildThresholdGeometry()
    {
        var geometry = new RectangleGeometry(new Rect(3.2, 26.9, 25.6, 2.4), 0.9, 0.9);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Renders the mark at a given pixel size.</summary>
    /// <param name="pixelSize">Edge length in physical pixels.</param>
    /// <param name="withPlate">
    /// Draw the navy plate behind the glyph. Without it the glyph is scaled up to fill the box,
    /// since the margin the plate needs is no longer doing anything.
    /// </param>
    /// <param name="monochrome">
    /// Flatten the gold to a single tone. Used for the muted "disconnected" tray icon.
    /// </param>
    public static BitmapSource Render(int pixelSize, bool withPlate = true, bool monochrome = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelSize, 1);

        double scale = pixelSize / DesignSize;

        var visual = new DrawingVisual();

        // Hinting off: the mark is pure geometry, and hinting flattens the arc into a polygon.
        RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);

        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));

            if (withPlate)
            {
                DrawPlate(dc);
            }
            else
            {
                PushFillTransform(dc);
            }

            Brush glyph = monochrome
                ? Frozen(new SolidColorBrush(Parse(GoldPrimary)))
                : BuildGoldBrush();

            dc.DrawGeometry(glyph, pen: null, BuildMarkGeometry());
            dc.DrawGeometry(glyph, pen: null, BuildThresholdGeometry());

            if (!withPlate)
            {
                dc.Pop();
            }

            dc.Pop();
        }

        var target = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();

        return target;
    }

    /// <summary>
    /// Scales and centres the glyph to fill the canvas, for renders that have no plate.
    /// </summary>
    /// <remarks>
    /// Pushes one transform group the caller must pop. Without this the unplated tray icon keeps
    /// the plate's margin and ends up noticeably smaller than every neighbouring tray icon.
    /// </remarks>
    private static void PushFillTransform(DrawingContext dc)
    {
        double available = DesignSize - (UnplatedMargin * 2);
        double factor = available / Math.Max(InkBounds.Width, InkBounds.Height);

        double centreX = InkBounds.X + (InkBounds.Width / 2);
        double centreY = InkBounds.Y + (InkBounds.Height / 2);
        double target = DesignSize / 2;

        var transform = new TransformGroup();

        // Move the ink's centre to the origin, scale about it, then move it to the canvas centre.
        transform.Children.Add(new TranslateTransform(-centreX, -centreY));
        transform.Children.Add(new ScaleTransform(factor, factor));
        transform.Children.Add(new TranslateTransform(target, target));
        transform.Freeze();

        dc.PushTransform(transform);
    }

    private static void DrawPlate(DrawingContext dc)
    {
        var plate = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            [
                new GradientStop(Parse(NavyPlateTop), 0),
                new GradientStop(Parse(NavyPlateBottom), 1),
            ],
        };
        plate.Freeze();

        var bounds = new Rect(0, 0, DesignSize, DesignSize);
        dc.DrawRoundedRectangle(plate, pen: null, bounds, 7, 7);

        // The gilding stroke. Without it the plate reads as a dark square rather than an object.
        var gild = new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x33, 0xD4, 0xAF, 0x37))), 1);
        gild.Freeze();

        dc.DrawRoundedRectangle(null, gild, new Rect(0.5, 0.5, DesignSize - 1, DesignSize - 1), 6.5, 6.5);
    }

    /// <summary>The brand's metallic gradient, running top-left to bottom-right.</summary>
    private static Brush BuildGoldBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            [
                new GradientStop(Parse(GoldHighlight), 0.00),
                new GradientStop(Parse(GoldPrimary), 0.45),
                new GradientStop(Parse(GoldDeep), 0.70),
                new GradientStop(Parse(GoldBright), 1.00),
            ],
        };
        brush.Freeze();
        return brush;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    // ==================================================================== ICO container

    /// <summary>
    /// The sizes a Windows application icon should carry.
    /// </summary>
    /// <remarks>
    /// 16 and 20 cover the tray and small shell surfaces, 24 and 32 the taskbar and title bar,
    /// 48 and 64 medium Explorer views, 128 and 256 large icons and the Store-style tiles.
    /// </remarks>
    public static readonly int[] IconSizes = [16, 20, 24, 32, 48, 64, 128, 256];

    /// <summary>
    /// Writes a multi-resolution <c>.ico</c>.
    /// </summary>
    /// <remarks>
    /// Every entry is stored as PNG. Windows Vista and later read PNG-compressed icon entries at
    /// any size, and doing so avoids hand-assembling the bottom-up DIB and AND-mask that the
    /// legacy BMP entry format requires.
    /// </remarks>
    public static void WriteIcoFile(string path, IEnumerable<int>? sizes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        int[] wanted = (sizes ?? IconSizes).Distinct().OrderBy(s => s).ToArray();

        if (wanted.Length == 0)
        {
            throw new ArgumentException("At least one icon size is required.", nameof(sizes));
        }

        var images = new List<(int Size, byte[] Png)>(wanted.Length);

        foreach (int size in wanted)
        {
            BitmapSource bitmap = Render(size);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var buffer = new MemoryStream();
            encoder.Save(buffer);
            images.Add((size, buffer.ToArray()));
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(file);

        // ICONDIR
        writer.Write((ushort)0);                 // Reserved
        writer.Write((ushort)1);                 // Type: 1 = icon
        writer.Write((ushort)images.Count);      // Number of images

        const int iconDirSize = 6;
        const int iconDirEntrySize = 16;
        int offset = iconDirSize + (iconDirEntrySize * images.Count);

        // ICONDIRENTRY per image
        foreach ((int size, byte[] png) in images)
        {
            // 256 is encoded as 0, which is why the field is a byte at all.
            writer.Write((byte)(size >= 256 ? 0 : size)); // Width
            writer.Write((byte)(size >= 256 ? 0 : size)); // Height
            writer.Write((byte)0);                        // Palette entries; 0 for truecolour
            writer.Write((byte)0);                        // Reserved
            writer.Write((ushort)1);                      // Colour planes
            writer.Write((ushort)32);                     // Bits per pixel
            writer.Write(png.Length);                     // Byte size of this image
            writer.Write(offset);                         // Offset of this image from file start

            offset += png.Length;
        }

        foreach ((_, byte[] png) in images)
        {
            writer.Write(png);
        }
    }

    /// <summary>Writes the mark as a standalone PNG, for documentation and the repository README.</summary>
    public static void WritePngFile(string path, int pixelSize, bool withPlate = true)
    {
        BitmapSource bitmap = Render(pixelSize, withPlate);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(file);
    }
}
