// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using FateTakesYouHome.Theming.Loading;
using FateTakesYouHome.Theming.Model;

namespace FateTakesYouHome.Theming.Rendering;

/// <summary>
/// Turns a resolved <see cref="Theme"/> into a WPF <see cref="ResourceDictionary"/>.
/// </summary>
/// <remarks>
/// Every brush, effect and easing produced here is frozen. Frozen freezables are thread-safe,
/// skip change notification, and let WPF cache their rendering — which matters because a theme
/// swap replaces every one of them at once.
/// </remarks>
public static class ThemeResourceBuilder
{
    /// <summary>Size of the tiling film-grain texture. Large enough to hide the repeat.</summary>
    private const int GrainTextureSize = 128;

    /// <summary>
    /// Fixed seed for the grain texture. Regenerating it per theme would make the noise crawl
    /// every time somebody nudged a colour in the editor.
    /// </summary>
    private const int GrainSeed = 0x5A17;

    private static ImageBrush? _cachedGrain;

    /// <summary>Builds the dictionary a theme publishes.</summary>
    public static ResourceDictionary Build(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var d = new ResourceDictionary
        {
            [ThemeKeys.Theme] = theme,
        };

        AddColours(d, theme);
        AddTypography(d, theme);
        AddShape(d, theme);
        AddMetrics(d, theme);
        AddMotion(d, theme);
        AddEffects(d, theme);
        AddOrnamentFlags(d, theme);
        AddButtonSurfaces(d, theme);

        return d;
    }

    private static void AddColours(ResourceDictionary d, Theme theme)
    {
        ThemeColors c = theme.Colors;

        Brush Solid(Color colour)
        {
            var brush = new SolidColorBrush(colour);
            brush.Freeze();
            return brush;
        }

        d[ThemeKeys.BrushSurfaceBase] = Solid(c.SurfaceBase);
        d[ThemeKeys.BrushSurfaceRaised] = Solid(c.SurfaceRaised);
        d[ThemeKeys.BrushSurfaceOverlay] = Solid(c.SurfaceOverlay);
        d[ThemeKeys.BrushSurfaceSunken] = Solid(c.SurfaceSunken);
        d[ThemeKeys.BrushSurfaceHighest] = Solid(c.SurfaceHighest);

        d[ThemeKeys.BrushBorderSubtle] = Solid(c.BorderSubtle);
        d[ThemeKeys.BrushBorderDefault] = Solid(c.BorderDefault);
        d[ThemeKeys.BrushBorderEmphasis] = Solid(c.BorderEmphasis);

        d[ThemeKeys.BrushTextPrimary] = Solid(c.TextPrimary);
        d[ThemeKeys.BrushTextMuted] = Solid(c.TextMuted);
        d[ThemeKeys.BrushTextFaint] = Solid(c.TextFaint);
        d[ThemeKeys.BrushTextInverse] = Solid(c.TextInverse);
        d[ThemeKeys.BrushTextAccent] = Solid(c.TextAccent);

        d[ThemeKeys.BrushAccentDefault] = Solid(c.AccentDefault);
        d[ThemeKeys.BrushAccentHover] = Solid(c.AccentHover);
        d[ThemeKeys.BrushAccentPressed] = Solid(c.AccentPressed);
        d[ThemeKeys.BrushAccentSubtle] = Solid(c.AccentSubtle);
        d[ThemeKeys.BrushAccentGlow] = Solid(c.AccentGlow);

        d[ThemeKeys.BrushStatusLive] = Solid(c.StatusLive);
        d[ThemeKeys.BrushStatusSuccess] = Solid(c.StatusSuccess);
        d[ThemeKeys.BrushStatusWarning] = Solid(c.StatusWarning);
        d[ThemeKeys.BrushStatusDanger] = Solid(c.StatusDanger);
        d[ThemeKeys.BrushStatusInfo] = Solid(c.StatusInfo);

        d[ThemeKeys.BrushPanelHighlight] = Solid(ColorParser.Parse(ThemeDefaults.PanelInnerHighlight));
        d[ThemeKeys.BrushPanelGild] = Solid(ColorParser.Parse(ThemeDefaults.PanelGildStroke));

        // Derived rather than declared: every theme gets a wash that matches its raised
        // surface, and a theme that wants a solid page can simply not use the starfield.
        Color wash = c.SurfaceRaised;
        wash.A = (byte)(wash.A * 0.80);
        d[ThemeKeys.BrushPanelWash] = Solid(wash);

        Color scrim = c.SurfaceBase;
        scrim.A = 0x99;
        d[ThemeKeys.BrushScrim] = Solid(scrim);

        d[ThemeKeys.BrushWindowBackground] = BuildWindowBackground(theme);
        d[ThemeKeys.BrushPanelEdge] = BuildPanelEdge(theme);
        d[ThemeKeys.BrushGoldGradient] = BuildGoldGradient(theme);
        d[ThemeKeys.BrushGoldSheen] = BuildGoldSheen(theme);
        d[ThemeKeys.BrushFilmGrain] = theme.Ornament.FilmGrain ? GetGrainBrush() : Brushes.Transparent;

        d[ThemeKeys.BrushStarfield] = theme.Ornament.Starfield
            ? StarfieldRenderer.ForTheme(theme)
            : Brushes.Transparent;

        d[ThemeKeys.ColorSurfaceBase] = c.SurfaceBase;
        d[ThemeKeys.ColorSurfaceRaised] = c.SurfaceRaised;
        d[ThemeKeys.ColorSurfaceOverlay] = c.SurfaceOverlay;
        d[ThemeKeys.ColorSurfaceHighest] = c.SurfaceHighest;
        d[ThemeKeys.ColorTextPrimary] = c.TextPrimary;
        d[ThemeKeys.ColorTextMuted] = c.TextMuted;
        d[ThemeKeys.ColorTextInverse] = c.TextInverse;
        d[ThemeKeys.ColorAccentDefault] = c.AccentDefault;
        d[ThemeKeys.ColorAccentHover] = c.AccentHover;
        d[ThemeKeys.ColorAccentPressed] = c.AccentPressed;
        d[ThemeKeys.ColorAccentGlow] = c.AccentGlow;
        d[ThemeKeys.ColorBorderDefault] = c.BorderDefault;
        d[ThemeKeys.ColorBorderEmphasis] = c.BorderEmphasis;
    }

    /// <summary>
    /// The base surface with the radial depth wash composited over it.
    /// </summary>
    /// <remarks>
    /// The brand forbids a flat navy fill, and WPF has no equivalent of stacking several CSS
    /// radial gradients on one element. A <see cref="DrawingBrush"/> holding one rectangle per
    /// layer reproduces it in a single brush the whole app can share.
    /// </remarks>
    private static Brush BuildWindowBackground(Theme theme)
    {
        var baseBrush = new SolidColorBrush(theme.Colors.SurfaceBase);

        if (!theme.Ornament.DepthWash || theme.Colors.DepthWash.Count == 0)
        {
            baseBrush.Freeze();
            return baseBrush;
        }

        var group = new DrawingGroup();
        var unitRect = new Rect(0, 0, 1, 1);

        group.Children.Add(new GeometryDrawing(baseBrush, pen: null, new RectangleGeometry(unitRect)));

        foreach (RadialWash wash in theme.Colors.DepthWash)
        {
            var gradient = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
                Center = new Point(wash.CenterX, wash.CenterY),
                GradientOrigin = new Point(wash.CenterX, wash.CenterY),
                RadiusX = wash.RadiusX,
                RadiusY = wash.RadiusY,
                GradientStops =
                [
                    new GradientStop(wash.Color, 0),

                    // The falloff is where CSS's "transparent 60%" lands. Below it the layer is
                    // still tinted; past it, gone.
                    new GradientStop(ColorParser.WithAlpha(wash.Color, 0), wash.Falloff),
                ],
            };
            gradient.Freeze();

            group.Children.Add(new GeometryDrawing(gradient, pen: null, new RectangleGeometry(unitRect)));
        }

        group.Freeze();

        var brush = new DrawingBrush(group)
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.None,
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = unitRect,
        };

        // A DrawingBrush re-runs its drawing on every paint unless it is cached. This one covers
        // the whole window and never changes, so rasterising it once is the difference between a
        // static background and a per-frame cost on every window in the application.
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);
        RenderOptions.SetCacheInvalidationThresholdMinimum(brush, 0.5);
        RenderOptions.SetCacheInvalidationThresholdMaximum(brush, 2.0);

        brush.Freeze();

        return brush;
    }

    private static Brush BuildPanelEdge(Theme theme)
    {
        if (theme.Ornament.PanelEdge == PanelEdge.Hairline)
        {
            var hairline = new SolidColorBrush(theme.Colors.BorderDefault);
            hairline.Freeze();
            return hairline;
        }

        // A gradient edge catches the light along one diagonal, which is what makes a ceremonial
        // panel read as a bevelled object rather than a rectangle with a border.
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            [
                new GradientStop(ColorParser.WithAlpha(theme.Colors.AccentHover, 0.55), 0),
                new GradientStop(ColorParser.WithAlpha(theme.Colors.BorderDefault, 0.65), 0.45),
                new GradientStop(ColorParser.WithAlpha(theme.Colors.AccentPressed, 0.35), 1),
            ],
        };
        gradient.Freeze();
        return gradient;
    }

    /// <summary>
    /// The metallic gradient for display type, derived from the theme's own accent ramp.
    /// </summary>
    /// <remarks>
    /// The brand states this as literal golds. Deriving it from the accent roles instead means a
    /// theme that recolours the accent to, say, brass gets a coherent brass gradient rather than
    /// gold text sitting on a brass button.
    /// </remarks>
    private static Brush BuildGoldGradient(Theme theme)
    {
        ThemeColors c = theme.Colors;

        // The brand's first stop is a tint lighter than any semantic role carries, so it is mixed.
        Color highlight = Mix(c.AccentHover, Colors.White, 0.45);

        var brush = new LinearGradientBrush
        {
            // CSS 135deg runs top-left to bottom-right.
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            [
                new GradientStop(highlight, 0.00),
                new GradientStop(c.AccentDefault, 0.45),
                new GradientStop(c.AccentPressed, 0.70),
                new GradientStop(c.AccentHover, 1.00),
            ],
        };
        brush.Freeze();
        return brush;
    }

    private static Brush BuildGoldSheen(Theme theme)
    {
        Color sheen = Mix(theme.Colors.AccentHover, Colors.White, 0.6);

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0.35),
            GradientStops =
            [
                new GradientStop(ColorParser.WithAlpha(sheen, 0), 0.30),
                new GradientStop(ColorParser.WithAlpha(sheen, 0.45), 0.50),
                new GradientStop(ColorParser.WithAlpha(sheen, 0), 0.70),
            ],
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Builds the tiling grain texture once and reuses it.
    /// </summary>
    /// <remarks>
    /// The grain is monochrome and drawn at low opacity by whoever consumes it, so it does not need
    /// to change with the palette — which is fortunate, because regenerating a bitmap on every
    /// keystroke in the theme editor would be visible.
    /// </remarks>
    private static ImageBrush GetGrainBrush()
    {
        if (_cachedGrain is not null)
        {
            return _cachedGrain;
        }

        const int stride = GrainTextureSize * 4;
        byte[] pixels = new byte[stride * GrainTextureSize];

        var random = new Random(GrainSeed);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            // Centre the noise on mid-grey so it neither lightens nor darkens the surface overall.
            byte value = (byte)random.Next(96, 160);

            pixels[i + 0] = value; // B
            pixels[i + 1] = value; // G
            pixels[i + 2] = value; // R
            pixels[i + 3] = 255;   // A
        }

        BitmapSource source = BitmapSource.Create(
            GrainTextureSize,
            GrainTextureSize,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);
        source.Freeze();

        var brush = new ImageBrush(source)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, GrainTextureSize, GrainTextureSize),
            Stretch = Stretch.None,
        };

        // Tiled over every panel in the application, so it is worth rasterising once.
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);

        brush.Freeze();

        _cachedGrain = brush;
        return brush;
    }

    private static void AddTypography(ResourceDictionary d, Theme theme)
    {
        ThemeTypography t = theme.Typography;

        d[ThemeKeys.FontDisplay] = ResolveFamily(t.DisplayFamily, "Georgia, serif");
        d[ThemeKeys.FontBody] = ResolveFamily(t.BodyFamily, "Segoe UI");
        d[ThemeKeys.FontProse] = ResolveFamily(t.ProseFamily, "Georgia, serif");
        d[ThemeKeys.FontMono] = ResolveFamily(t.MonoFamily, "Consolas");

        d[ThemeKeys.SizeCaption] = t.Scaled(t.SizeCaption);
        d[ThemeKeys.SizeBody] = t.Scaled(t.SizeBody);
        d[ThemeKeys.SizeSubtitle] = t.Scaled(t.SizeSubtitle);
        d[ThemeKeys.SizeTitle] = t.Scaled(t.SizeTitle);
        d[ThemeKeys.SizeDisplay] = t.Scaled(t.SizeDisplay);

        d[ThemeKeys.TrackingWordmark] = t.TrackingWordmark;
        d[ThemeKeys.TrackingLabel] = t.TrackingLabel;
        d[ThemeKeys.TrackingDisplay] = t.TrackingDisplay;
    }

    /// <summary>
    /// Where embedded typefaces live, as a pack URI ending in a slash.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set by the host application at startup. Left null — as it is in tests, in the icon
    /// generator, and anywhere without a WPF <c>Application</c> — font names resolve against
    /// installed families only.
    /// </para>
    /// <para>
    /// It is a property rather than a constant because this library must not know the name of the
    /// assembly that consumes it. Hard-coding the application's assembly name here would make the
    /// theming library unusable from anything else, including its own tests.
    /// </para>
    /// </remarks>
    public static Uri? EmbeddedFontBaseUri { get; set; }

    /// <summary>
    /// Resolves a family name against the embedded fonts, then the system.
    /// </summary>
    /// <remarks>
    /// The comma-separated list is WPF's own fallback syntax: the embedded face first, then an
    /// installed family of the same name, then something guaranteed to exist. A theme naming a
    /// font nobody has degrades to readable rather than to WPF's default, which is Times.
    /// </remarks>
    private static FontFamily ResolveFamily(string requested, string fallback)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return new FontFamily(fallback);
        }

        if (EmbeddedFontBaseUri is { } baseUri)
        {
            try
            {
                return new FontFamily(baseUri, $"./#{requested}, {requested}, {fallback}");
            }
            catch (Exception ex) when (ex is UriFormatException or NotSupportedException)
            {
                // The pack scheme is only registered once WPF's application infrastructure has
                // initialised. Falling through costs the embedded face, not the whole theme.
            }
        }

        return new FontFamily($"{requested}, {fallback}");
    }

    private static void AddShape(ResourceDictionary d, Theme theme)
    {
        ThemeShape s = theme.Shape;

        d[ThemeKeys.RadiusSm] = new CornerRadius(s.RadiusSm);
        d[ThemeKeys.RadiusMd] = new CornerRadius(s.RadiusMd);
        d[ThemeKeys.RadiusLg] = new CornerRadius(s.RadiusLg);
        d[ThemeKeys.RadiusPill] = new CornerRadius(s.RadiusPill);
        d[ThemeKeys.RadiusFlyout] = new CornerRadius(s.FlyoutRadius);
        d[ThemeKeys.RadiusButton] = new CornerRadius(
            theme.Buttons.Style == ButtonStyle.Pill
                ? s.RadiusPill
                : theme.Buttons.Radius ?? s.RadiusMd);

        d[ThemeKeys.ThicknessStroke] = new Thickness(s.StrokeThickness);
        d[ThemeKeys.ThicknessButtonPadding] = new Thickness(
            theme.Buttons.Padding * 1.4, theme.Buttons.Padding * 0.7,
            theme.Buttons.Padding * 1.4, theme.Buttons.Padding * 0.7);
    }

    private static void AddMetrics(ResourceDictionary d, Theme theme)
    {
        d[ThemeKeys.MetricFlyoutWidth] = theme.Shape.FlyoutWidth;
        d[ThemeKeys.MetricFlyoutMaxHeight] = theme.Shape.FlyoutMaxHeight;
        d[ThemeKeys.MetricFlyoutMargin] = theme.Shape.FlyoutMargin;
        d[ThemeKeys.MetricTileHeight] = theme.Shape.TileHeight;
        d[ThemeKeys.MetricHoverLift] = theme.Buttons.HoverLift;
        d[ThemeKeys.MetricPressScale] = theme.Buttons.PressScale;
        d[ThemeKeys.MetricFlyoutTravel] = theme.Motion.FlyoutTravel;
        d[ThemeKeys.MetricFlyoutScaleFrom] = theme.Motion.FlyoutScaleFrom;
    }

    private static void AddMotion(ResourceDictionary d, Theme theme)
    {
        ThemeMotion m = theme.Motion;
        bool animate = m.Enabled;

        Duration Of(TimeSpan span) => new(animate ? span : TimeSpan.Zero);

        d[ThemeKeys.DurationFlyoutOpen] = Of(m.FlyoutOpen);
        d[ThemeKeys.DurationFlyoutClose] = Of(m.FlyoutClose);
        d[ThemeKeys.DurationHover] = Of(m.Hover);
        d[ThemeKeys.DurationPress] = Of(m.Press);
        d[ThemeKeys.DurationPageTransition] = Of(m.PageTransition);
        d[ThemeKeys.DurationStaggerStep] = Of(m.StaggerStep);

        d[ThemeKeys.EasingFlyout] = CubicBezierEase.Create(m.FlyoutEasing);
        d[ThemeKeys.EasingStandard] = CubicBezierEase.Create(m.StandardEasing);

        d[ThemeKeys.MotionEnabled] = animate;
    }

    private static void AddEffects(ResourceDictionary d, Theme theme)
    {
        ThemeBackdrop b = theme.Backdrop;

        var shadow = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = b.ShadowBlur,
            ShadowDepth = b.ShadowDepth,
            Direction = 270, // Straight down, matching the CSS shadow's positive Y offset.
            Opacity = b.ShadowOpacity,
            RenderingBias = RenderingBias.Quality,
        };
        shadow.Freeze();
        d[ThemeKeys.EffectPanelShadow] = shadow;

        var glow = new DropShadowEffect
        {
            Color = theme.Colors.AccentGlow,
            BlurRadius = 18,
            ShadowDepth = 0,
            Opacity = 0.85,
            RenderingBias = RenderingBias.Performance,
        };
        glow.Freeze();
        d[ThemeKeys.EffectAccentGlow] = glow;
    }

    private static void AddOrnamentFlags(ResourceDictionary d, Theme theme)
    {
        ThemeOrnament o = theme.Ornament;

        d[ThemeKeys.OrnamentCornerBrackets] = o.CornerBrackets;
        d[ThemeKeys.OrnamentFilmGrain] = o.FilmGrain;
        d[ThemeKeys.OrnamentGlassmorphism] = o.Glassmorphism;
        d[ThemeKeys.OrnamentOrnateDividers] = o.OrnateDividers;
        d[ThemeKeys.OrnamentAmbientMotion] = o.AmbientMotion;
        d[ThemeKeys.OrnamentStaggerEntrances] = o.StaggerEntrances;
        d[ThemeKeys.OrnamentGradientDisplayFill] = o.GradientDisplayFill;
        d[ThemeKeys.OrnamentDepthWash] = o.DepthWash;

        d[ThemeKeys.VisibilityCornerBrackets] = Show(o.CornerBrackets);
        d[ThemeKeys.VisibilityFilmGrain] = Show(o.FilmGrain);
        d[ThemeKeys.VisibilityOrnateDividers] = Show(o.OrnateDividers);
        d[ThemeKeys.VisibilityGlassmorphism] = Show(o.Glassmorphism);
        d[ThemeKeys.VisibilityStarfield] = Show(o.Starfield);
        d[ThemeKeys.OpacityFilmGrain] = o.FilmGrain ? o.FilmGrainOpacity : 0d;

        d[ThemeKeys.ButtonActiveGlow] = theme.Buttons.ActiveGlow;
        d[ThemeKeys.ButtonHoverSheen] = theme.Buttons.HoverSheen;
        d[ThemeKeys.ButtonStyleName] = theme.Buttons.Style.ToString();
    }

    /// <summary>
    /// Collapses <c>buttons.style</c> into the concrete brushes one template can consume.
    /// </summary>
    /// <remarks>
    /// The alternative — a control template per style, selected by a trigger — means six templates
    /// to keep in step every time the button gains a state. Resolving to brushes here keeps the
    /// visual vocabulary in one place and makes a new button style a change to this method alone.
    /// </remarks>
    private static void AddButtonSurfaces(ResourceDictionary d, Theme theme)
    {
        ThemeColors c = theme.Colors;

        Brush Solid(Color colour)
        {
            var brush = new SolidColorBrush(colour);
            brush.Freeze();
            return brush;
        }

        Color transparent = Colors.Transparent;
        transparent.A = 0;

        Color face, faceHover, facePressed, faceActive;
        Color stroke, strokeHover, strokeActive;
        Color text, textHover, textActive;
        Color topLight = transparent;

        switch (theme.Buttons.Style)
        {
            case ButtonStyle.Foil:
                // Gold as material: a solid accent slab that darkens as it is pressed.
                face = c.AccentDefault;
                faceHover = c.AccentHover;
                facePressed = c.AccentPressed;
                faceActive = c.AccentHover;
                stroke = c.AccentPressed;
                strokeHover = c.AccentHover;
                strokeActive = c.AccentHover;
                text = c.TextInverse;
                textHover = c.TextInverse;
                textActive = c.TextInverse;
                break;

            case ButtonStyle.Glow:
                // Gold as light: the surface stays dark and the accent arrives as a halo.
                face = c.SurfaceRaised;
                faceHover = c.AccentSubtle;
                facePressed = ColorParser.WithAlpha(c.AccentPressed, 0.24);
                faceActive = c.AccentSubtle;
                stroke = c.BorderSubtle;
                strokeHover = ColorParser.WithAlpha(c.AccentDefault, 0.55);
                strokeActive = c.AccentDefault;
                text = c.TextPrimary;
                textHover = c.TextAccent;
                textActive = c.TextAccent;
                break;

            case ButtonStyle.Ghost:
                // Nothing until you approach it.
                face = transparent;
                faceHover = c.AccentSubtle;
                facePressed = ColorParser.WithAlpha(c.AccentPressed, 0.20);
                faceActive = c.AccentSubtle;
                stroke = transparent;
                strokeHover = transparent;
                strokeActive = ColorParser.WithAlpha(c.AccentDefault, 0.45);
                text = c.TextPrimary;
                textHover = c.TextAccent;
                textActive = c.TextAccent;
                break;

            case ButtonStyle.Outline:
                face = transparent;
                faceHover = c.AccentSubtle;
                facePressed = ColorParser.WithAlpha(c.AccentPressed, 0.20);
                faceActive = c.AccentSubtle;
                stroke = c.BorderDefault;
                strokeHover = c.AccentDefault;
                strokeActive = c.AccentDefault;
                text = c.TextPrimary;
                textHover = c.TextAccent;
                textActive = c.TextAccent;
                break;

            case ButtonStyle.Pill:
                face = c.AccentDefault;
                faceHover = c.AccentHover;
                facePressed = c.AccentPressed;
                faceActive = c.AccentHover;
                stroke = transparent;
                strokeHover = transparent;
                strokeActive = transparent;
                text = c.TextInverse;
                textHover = c.TextInverse;
                textActive = c.TextInverse;
                break;

            default: // Engraved
                // Gold as an engraved line: a recessed face with a lit top edge, the accent
                // appearing only as the stroke around it.
                face = c.SurfaceRaised;
                faceHover = c.SurfaceOverlay;
                facePressed = c.SurfaceSunken;
                faceActive = c.AccentSubtle;
                stroke = c.BorderDefault;
                strokeHover = ColorParser.WithAlpha(c.AccentDefault, 0.50);
                strokeActive = c.AccentDefault;
                text = c.TextPrimary;
                textHover = c.TextPrimary;
                textActive = c.TextAccent;

                // The single inset highlight from the brand's gilded panel shadow. It is what
                // makes the face read as recessed rather than merely darker.
                topLight = ColorParser.Parse(ThemeDefaults.PanelInnerHighlight);
                break;
        }

        d[ThemeKeys.BrushButtonFace] = Solid(face);
        d[ThemeKeys.BrushButtonFaceHover] = Solid(faceHover);
        d[ThemeKeys.BrushButtonFacePressed] = Solid(facePressed);
        d[ThemeKeys.BrushButtonFaceActive] = Solid(faceActive);

        // Disabled is the resting face flattened towards the base surface, so it recedes without
        // becoming a different shape.
        d[ThemeKeys.BrushButtonFaceDisabled] = Solid(Mix(face, c.SurfaceBase, 0.6));

        d[ThemeKeys.BrushButtonStroke] = Solid(stroke);
        d[ThemeKeys.BrushButtonStrokeHover] = Solid(strokeHover);
        d[ThemeKeys.BrushButtonStrokeActive] = Solid(strokeActive);

        d[ThemeKeys.BrushButtonText] = Solid(text);
        d[ThemeKeys.BrushButtonTextHover] = Solid(textHover);
        d[ThemeKeys.BrushButtonTextActive] = Solid(textActive);
        d[ThemeKeys.BrushButtonTextDisabled] = Solid(c.TextFaint);

        d[ThemeKeys.BrushButtonTopLight] = Solid(topLight);
    }

    /// <summary>Collapsed rather than Hidden: a switched-off ornament must not reserve layout space.</summary>
    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Linear blend between two colours in straight sRGB.</summary>
    private static Color Mix(Color a, Color b, double amount)
    {
        double t = Math.Clamp(amount, 0, 1);

        return Color.FromArgb(
            (byte)Math.Round(a.A + ((b.A - a.A) * t)),
            (byte)Math.Round(a.R + ((b.R - a.R) * t)),
            (byte)Math.Round(a.G + ((b.G - a.G) * t)),
            (byte)Math.Round(a.B + ((b.B - a.B) * t)));
    }
}
