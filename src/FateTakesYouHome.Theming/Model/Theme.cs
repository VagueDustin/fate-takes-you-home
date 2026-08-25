// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows.Media;

namespace FateTakesYouHome.Theming.Model;

/// <summary>
/// A fully resolved theme: every value present, inheritance already collapsed, colours parsed.
/// </summary>
/// <remarks>
/// <see cref="ThemeDocument"/> is what a person writes; this is what the application renders. The
/// split means a theme file can be a two-line patch while the renderer never has to reason about a
/// missing value.
/// </remarks>
public sealed class Theme
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Author { get; init; } = "Unknown";

    public string Version { get; init; } = "1.0.0";

    public string Description { get; init; } = string.Empty;

    public string? Homepage { get; init; }

    /// <summary>True for themes compiled into the application. These cannot be edited in place.</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>Where the theme was loaded from, when it came from a file.</summary>
    public string? SourcePath { get; init; }

    /// <summary>The chain of ids this theme inherited from, nearest ancestor first.</summary>
    public IReadOnlyList<string> InheritanceChain { get; init; } = [];

    public ThemeAppearance Appearance { get; init; } = ThemeAppearance.Dark;

    public OrnamentTier Tier { get; init; } = OrnamentTier.Charted;

    public required ThemeColors Colors { get; init; }

    public required ThemeTypography Typography { get; init; }

    public required ThemeShape Shape { get; init; }

    public required ThemeMotion Motion { get; init; }

    public required ThemeOrnament Ornament { get; init; }

    public required ThemeButtons Buttons { get; init; }

    public required ThemeBackdrop Backdrop { get; init; }
}

public sealed class ThemeColors
{
    public required Color SurfaceBase { get; init; }
    public required Color SurfaceRaised { get; init; }
    public required Color SurfaceOverlay { get; init; }
    public required Color SurfaceSunken { get; init; }
    public required Color SurfaceHighest { get; init; }

    public required Color BorderSubtle { get; init; }
    public required Color BorderDefault { get; init; }
    public required Color BorderEmphasis { get; init; }

    public required Color TextPrimary { get; init; }
    public required Color TextMuted { get; init; }
    public required Color TextFaint { get; init; }
    public required Color TextInverse { get; init; }
    public required Color TextAccent { get; init; }

    public required Color AccentDefault { get; init; }
    public required Color AccentHover { get; init; }
    public required Color AccentPressed { get; init; }
    public required Color AccentSubtle { get; init; }
    public required Color AccentGlow { get; init; }

    public required Color StatusLive { get; init; }
    public required Color StatusSuccess { get; init; }
    public required Color StatusWarning { get; init; }
    public required Color StatusDanger { get; init; }
    public required Color StatusInfo { get; init; }

    public IReadOnlyList<RadialWash> DepthWash { get; init; } = [];
}

/// <summary>One radial layer of the background depth wash, in surface-relative coordinates.</summary>
public sealed record RadialWash(
    double CenterX,
    double CenterY,
    double RadiusX,
    double RadiusY,
    Color Color,
    double Falloff);

public sealed class ThemeTypography
{
    public required string DisplayFamily { get; init; }
    public required string BodyFamily { get; init; }
    public required string ProseFamily { get; init; }
    public required string MonoFamily { get; init; }

    public double TrackingWordmark { get; init; } = 0.14;
    public double TrackingLabel { get; init; } = 0.34;
    public double TrackingDisplay { get; init; } = 0.02;

    public double Scale { get; init; } = 1.0;

    public double SizeCaption { get; init; } = 11;
    public double SizeBody { get; init; } = 13;
    public double SizeSubtitle { get; init; } = 15;
    public double SizeTitle { get; init; } = 20;
    public double SizeDisplay { get; init; } = 28;

    /// <summary>Applies <see cref="Scale"/> and rounds to a value WPF will not blur.</summary>
    public double Scaled(double size) => Math.Round(size * Scale, 2);
}

public sealed class ThemeShape
{
    public double RadiusSm { get; init; } = 6;
    public double RadiusMd { get; init; } = 10;
    public double RadiusLg { get; init; } = 16;
    public double RadiusPill { get; init; } = 999;
    public double StrokeThickness { get; init; } = 1;

    public double FlyoutRadius { get; init; } = 12;
    public double FlyoutWidth { get; init; } = 360;
    public double FlyoutMaxHeight { get; init; } = 620;
    public double FlyoutMargin { get; init; } = 12;
    public double TileHeight { get; init; } = 52;
}

public sealed class ThemeMotion
{
    public bool Enabled { get; init; } = true;
    public double SpeedScale { get; init; } = 1.0;

    public TimeSpan FlyoutOpen { get; init; } = TimeSpan.FromMilliseconds(220);
    public TimeSpan FlyoutClose { get; init; } = TimeSpan.FromMilliseconds(140);
    public TimeSpan Hover { get; init; } = TimeSpan.FromMilliseconds(120);
    public TimeSpan Press { get; init; } = TimeSpan.FromMilliseconds(70);
    public TimeSpan PageTransition { get; init; } = TimeSpan.FromMilliseconds(240);
    public TimeSpan StaggerStep { get; init; } = TimeSpan.FromMilliseconds(24);

    public int StaggerMaxItems { get; init; } = 12;

    public EasingSpec FlyoutEasing { get; init; } = EasingSpec.FateOut;
    public EasingSpec StandardEasing { get; init; } = EasingSpec.FateOut;

    public double FlyoutTravel { get; init; } = 14;
    public double FlyoutScaleFrom { get; init; } = 0.97;

    public bool RespectSystemReducedMotion { get; init; } = true;
}

/// <summary>
/// A cubic Bézier easing curve, expressed the same way CSS does.
/// </summary>
/// <remarks>
/// WPF ships no cubic-bezier easing, so the brand's <c>--ease</c> and <c>--spring</c> curves cannot
/// be reproduced with the built-in functions. Carrying the control points here lets the renderer
/// build a real one.
/// </remarks>
public readonly record struct EasingSpec(double X1, double Y1, double X2, double Y2)
{
    /// <summary>The brand's standard curve, <c>cubic-bezier(0.2, 0.7, 0.3, 1)</c>.</summary>
    public static readonly EasingSpec FateOut = new(0.2, 0.7, 0.3, 1.0);

    /// <summary>The brand's overshoot curve, <c>cubic-bezier(0.3, 1.5, 0.4, 1)</c>.</summary>
    public static readonly EasingSpec FateSpring = new(0.3, 1.5, 0.4, 1.0);

    public static readonly EasingSpec Linear = new(0, 0, 1, 1);
    public static readonly EasingSpec EaseOut = new(0, 0, 0.58, 1);
    public static readonly EasingSpec EaseIn = new(0.42, 0, 1, 1);
    public static readonly EasingSpec EaseInOut = new(0.42, 0, 0.58, 1);

    public override string ToString() =>
        $"cubic-bezier({X1:0.###}, {Y1:0.###}, {X2:0.###}, {Y2:0.###})";
}

public sealed class ThemeOrnament
{
    public bool CornerBrackets { get; init; }
    public bool FilmGrain { get; init; }
    public double FilmGrainOpacity { get; init; } = 0.035;
    public bool Glassmorphism { get; init; }
    public bool OrnateDividers { get; init; }
    public bool AmbientMotion { get; init; }
    public bool StaggerEntrances { get; init; }
    public bool GradientDisplayFill { get; init; }
    public bool DepthWash { get; init; } = true;

    /// <summary>Star field behind the full window.</summary>
    public bool Starfield { get; init; }
    public PanelEdge PanelEdge { get; init; } = PanelEdge.Hairline;
    public int MaxConcurrentAnimations { get; init; } = 3;

    /// <summary>The switch defaults the brand contract prescribes for a tier.</summary>
    public static ThemeOrnament ForTier(OrnamentTier tier) => tier switch
    {
        OrnamentTier.Ceremonial => new ThemeOrnament
        {
            CornerBrackets = true,
            FilmGrain = true,
            Glassmorphism = true,
            OrnateDividers = true,
            AmbientMotion = true,
            StaggerEntrances = true,
            GradientDisplayFill = true,
            DepthWash = true,
            Starfield = true,
            PanelEdge = PanelEdge.Gradient,
            MaxConcurrentAnimations = 5,
        },
        OrnamentTier.Charted => new ThemeOrnament
        {
            CornerBrackets = true,
            FilmGrain = true,
            Glassmorphism = true,
            OrnateDividers = false,
            AmbientMotion = false,
            StaggerEntrances = true,
            GradientDisplayFill = false,
            DepthWash = true,
            Starfield = true,
            PanelEdge = PanelEdge.Hairline,
            MaxConcurrentAnimations = 3,
        },
        _ => new ThemeOrnament
        {
            CornerBrackets = false,
            FilmGrain = false,
            Glassmorphism = false,
            OrnateDividers = false,
            AmbientMotion = false,
            StaggerEntrances = false,
            GradientDisplayFill = false,
            DepthWash = true,
            Starfield = false,
            PanelEdge = PanelEdge.Hairline,
            MaxConcurrentAnimations = 1,
        },
    };
}

public sealed class ThemeButtons
{
    public ButtonStyle Style { get; init; } = ButtonStyle.Engraved;
    public double? Radius { get; init; }
    public double HoverLift { get; init; } = 1;
    public double PressScale { get; init; } = 0.985;
    public bool ActiveGlow { get; init; } = true;
    public bool HoverSheen { get; init; }
    public double Padding { get; init; } = 10;
}

public sealed class ThemeBackdrop
{
    public BackdropMode Mode { get; init; } = BackdropMode.Composited;
    public double TintOpacity { get; init; } = 0.86;
    public double FlyoutOpacity { get; init; } = 1.0;
    public double ShadowBlur { get; init; } = 34;
    public double ShadowOpacity { get; init; } = 0.7;
    public double ShadowDepth { get; init; } = 10;
}
