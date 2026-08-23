using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FateTakesYouHome.Theming.Model;

namespace FateTakesYouHome.Theming.Rendering;

/// <summary>
/// Draws the night sky that sits behind the interface.
/// </summary>
/// <remarks>
/// <para>
/// The house look is a deep field with scattered stars and faint constellation lines — the thing
/// that makes a navy background read as depth rather than as a flat fill. It is rendered once to a
/// bitmap when a theme is applied and then treated as an image, so the cost is a few tens of
/// milliseconds at theme-change time and nothing at all per frame.
/// </para>
/// <para>
/// Everything is derived from a fixed seed. A starfield that reshuffled itself on every theme
/// tweak — or worse, on every window resize — would be a distraction rather than a backdrop.
/// </para>
/// </remarks>
public static class StarfieldRenderer
{
    /// <summary>
    /// Render size. Not the display size: the brush stretches to fill.
    /// </summary>
    /// <remarks>
    /// 16:9 at a middling resolution. Large enough that stars stay small points when stretched
    /// across a wide window, small enough that generating it is not noticeable.
    /// </remarks>
    private const int Width = 1600;
    private const int Height = 900;

    /// <summary>Fixed so the sky is the same every time it is drawn.</summary>
    private const int Seed = 0x5EED;

    private const int StarCount = 460;

    /// <summary>How many stars get a visible glow rather than being a bare point.</summary>
    private const int BrightStarCount = 34;

    /// <summary>Groups of nearby stars joined by a hairline.</summary>
    private const int ConstellationCount = 7;

    /// <summary>Rendered fields, keyed by the colours that went into them.</summary>
    private static readonly Dictionary<(uint Ink, uint Accent), ImageBrush> Cache = [];

    private static readonly object Gate = new();

    /// <summary>
    /// Returns the starfield for a theme, rendering it on first use.
    /// </summary>
    /// <remarks>
    /// Cached by colour rather than by theme, so the six shipped themes that share a palette share
    /// one bitmap, and flicking between them costs nothing.
    /// </remarks>
    public static ImageBrush ForTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Color ink = theme.Colors.TextPrimary;
        Color accent = theme.Colors.AccentDefault;

        (uint, uint) key = (Pack(ink), Pack(accent));

        lock (Gate)
        {
            if (Cache.TryGetValue(key, out ImageBrush? cached))
            {
                return cached;
            }

            ImageBrush built = Render(ink, accent);
            Cache[key] = built;
            return built;
        }
    }

    private static ImageBrush Render(Color ink, Color accent)
    {
        var random = new Random(Seed);
        var stars = new List<Star>(StarCount);

        for (int i = 0; i < StarCount; i++)
        {
            // Brightness is skewed low: a sky of uniformly bright stars reads as noise. Cubing a
            // uniform value pushes most stars faint and leaves a few standing out.
            double roll = random.NextDouble();
            double brightness = roll * roll * roll;

            stars.Add(new Star(
                X: random.NextDouble() * Width,
                Y: random.NextDouble() * Height,
                Radius: 0.5 + (brightness * 1.5),
                Alpha: 0.16 + (brightness * 0.80),

                // A minority are gold. All-gold looks like glitter; all-white looks clinical.
                IsAccent: random.NextDouble() < 0.22));
        }

        var visual = new DrawingVisual();

        using (DrawingContext dc = visual.RenderOpen())
        {
            // The bitmap is an overlay, so it starts transparent — the base surface and the depth
            // wash are painted underneath by the window background brush.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height));

            DrawConstellations(dc, stars, random, ink);

            foreach (Star star in stars)
            {
                Color colour = star.IsAccent ? accent : ink;
                DrawStar(dc, star, colour);
            }

            DrawGlints(dc, stars, random, ink, accent);
        }

        var target = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();

        var brush = new ImageBrush(target)
        {
            // UniformToFill so stars stay round whatever the window's aspect ratio. Tiling would
            // repeat the constellations visibly.
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TileMode = TileMode.None,
        };

        // Rasterised once. Without this the brush is re-sampled on every paint of every surface
        // that uses it.
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);

        brush.Freeze();
        return brush;
    }

    private static void DrawStar(DrawingContext dc, Star star, Color colour)
    {
        var centre = new Point(star.X, star.Y);

        // Faint stars are a flat dot; anything brighter gets a soft falloff, which is what stops
        // them looking like dust on the screen.
        if (star.Alpha < 0.35)
        {
            var flat = new SolidColorBrush(ColorParser.WithAlpha(colour, star.Alpha));
            flat.Freeze();

            dc.DrawEllipse(flat, null, centre, star.Radius, star.Radius);
            return;
        }

        var glow = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops =
            [
                new GradientStop(ColorParser.WithAlpha(colour, star.Alpha), 0),
                new GradientStop(ColorParser.WithAlpha(colour, star.Alpha * 0.45), 0.35),
                new GradientStop(ColorParser.WithAlpha(colour, 0), 1),
            ],
        };
        glow.Freeze();

        double halo = star.Radius * 4.5;
        dc.DrawEllipse(glow, null, centre, halo, halo);

        var core = new SolidColorBrush(ColorParser.WithAlpha(colour, Math.Min(1, star.Alpha + 0.2)));
        core.Freeze();

        dc.DrawEllipse(core, null, centre, star.Radius * 0.8, star.Radius * 0.8);
    }

    /// <summary>Adds a four-point cross to the very brightest stars.</summary>
    private static void DrawGlints(
        DrawingContext dc, List<Star> stars, Random random, Color ink, Color accent)
    {
        foreach (Star star in stars
                     .OrderByDescending(s => s.Alpha)
                     .Take(BrightStarCount))
        {
            Color colour = star.IsAccent ? accent : ink;

            var pen = new Pen(
                new SolidColorBrush(ColorParser.WithAlpha(colour, star.Alpha * 0.5)), 0.7);
            pen.Freeze();

            double arm = star.Radius * (3.5 + (random.NextDouble() * 2.5));

            dc.DrawLine(pen, new Point(star.X - arm, star.Y), new Point(star.X + arm, star.Y));
            dc.DrawLine(pen, new Point(star.X, star.Y - arm), new Point(star.X, star.Y + arm));
        }
    }

    /// <summary>
    /// Joins a few clusters of nearby stars with hairlines.
    /// </summary>
    /// <remarks>
    /// Built from actual proximity rather than drawn at random, so the shapes look like something
    /// somebody might have traced rather than a scribble laid over the sky.
    /// </remarks>
    private static void DrawConstellations(
        DrawingContext dc, List<Star> stars, Random random, Color ink)
    {
        var pen = new Pen(new SolidColorBrush(ColorParser.WithAlpha(ink, 0.16)), 0.7);
        pen.Freeze();

        var used = new HashSet<int>();

        for (int c = 0; c < ConstellationCount; c++)
        {
            int start = random.Next(stars.Count);

            if (!used.Add(start))
            {
                continue;
            }

            int links = 2 + random.Next(4);
            int current = start;

            for (int link = 0; link < links; link++)
            {
                int next = NearestUnused(stars, current, used);

                if (next < 0)
                {
                    break;
                }

                used.Add(next);

                dc.DrawLine(
                    pen,
                    new Point(stars[current].X, stars[current].Y),
                    new Point(stars[next].X, stars[next].Y));

                current = next;
            }
        }
    }

    /// <summary>The closest star not already part of a constellation, within a sane distance.</summary>
    private static int NearestUnused(List<Star> stars, int from, HashSet<int> used)
    {
        // Capped so a constellation cannot stretch a line across the whole sky to find a partner.
        const double maxDistanceSquared = 260 * 260;

        int best = -1;
        double bestDistance = maxDistanceSquared;

        Star origin = stars[from];

        for (int i = 0; i < stars.Count; i++)
        {
            if (i == from || used.Contains(i))
            {
                continue;
            }

            double dx = stars[i].X - origin.X;
            double dy = stars[i].Y - origin.Y;
            double distance = (dx * dx) + (dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private static uint Pack(Color c) =>
        ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private readonly record struct Star(double X, double Y, double Radius, double Alpha, bool IsAccent);
}
