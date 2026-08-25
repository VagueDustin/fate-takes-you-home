// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace FateTakesYouHome.Theming.Loading;

/// <summary>
/// The compiled-in baseline every theme inherits from: FATE, charted tier.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This file is the only legal home for a colour literal in this codebase.</strong> It
/// mirrors the role of <c>src/primitives.ts</c> in the <c>vaguedustin-brand</c> repository: the
/// values are written down exactly once, and everything else in the application consumes them
/// through semantic roles. A hex anywhere else is a bug.
/// </para>
/// <para>
/// These values are duplicated in <c>themes/fate.json</c>, which is the shipped, user-visible copy.
/// The two are held in sync by <c>ThemeDefaultsTests</c> — if you retune one, the test tells you to
/// retune the other. The compiled copy exists so the application can never end up themeless when a
/// file is missing or corrupt.
/// </para>
/// </remarks>
public static class ThemeDefaults
{
    public const string FateThemeId = "fate";

    // -- Surfaces ---------------------------------------------------------------------------

    public const string SurfaceBase = "#070B1A";
    public const string SurfaceRaised = "#101736";
    public const string SurfaceOverlay = "#16213E";
    public const string SurfaceSunken = "#020617";
    public const string SurfaceHighest = "#1E2A4A";

    // -- Borders ----------------------------------------------------------------------------

    public const string BorderSubtle = "rgba(212, 175, 55, 0.10)";
    public const string BorderDefault = "#22325C";
    public const string BorderEmphasis = "#D4AF37";

    // -- Foreground -------------------------------------------------------------------------
    // Cool temperature throughout. Never mixed with the warm parchment ramp in one product.

    public const string TextPrimary = "#E6EAF2";
    public const string TextMuted = "#94A0BB";

    /// <remarks>
    /// #76849F, not #5C6B8A. The darker value fails WCAG AA against navy-950 at roughly 3.8:1.
    /// The brand contract calls this out as a regression that has already been made once.
    /// </remarks>
    public const string TextFaint = "#76849F";

    public const string TextInverse = "#070B1A";
    public const string TextAccent = "#F2C94C";

    // -- Accent -----------------------------------------------------------------------------
    // Gold means interactive or brand. Never status.

    public const string AccentDefault = "#D4AF37";
    public const string AccentHover = "#F2C94C";
    public const string AccentPressed = "#B8902B";
    public const string AccentSubtle = "rgba(212, 175, 55, 0.12)";
    public const string AccentGlow = "rgba(212, 175, 55, 0.45)";

    // -- Status -----------------------------------------------------------------------------
    // "Live" is red, industry-wide. It does not get branded gold.

    public const string StatusLive = "#E8617A";
    public const string StatusSuccess = "#A6E26A";
    public const string StatusWarning = "#FFD166";
    public const string StatusDanger = "#E8617A";
    public const string StatusInfo = "#6FB1FF";

    // -- Depth wash -------------------------------------------------------------------------

    /// <summary>
    /// The three radial layers the brand requires over the flat navy base.
    /// </summary>
    /// <remarks>
    /// The brand states two of these in pixels (<c>1100px 700px</c>, <c>900px 800px</c>) because it
    /// was written for full-page web layouts. A tray flyout is 360px wide, so the pixel radii are
    /// re-expressed here as fractions of the surface. That keeps the character of the wash at any
    /// size instead of blowing one lobe across the whole panel.
    /// </remarks>
    public static readonly (double X, double Y, double Rx, double Ry, string Color, double Falloff)[] DepthWash =
    [
        (0.50, -0.10, 0.80, 0.50, "rgba(212, 175, 55, 0.06)", 1.00),
        (0.15, -0.10, 0.90, 0.95, "rgba(42, 79, 150, 0.32)", 0.60),
        (1.10, 0.08, 0.75, 1.10, "rgba(20, 42, 92, 0.55)", 0.55),
    ];

    // -- Typography -------------------------------------------------------------------------

    public const string DisplayFamily = "Cinzel";
    public const string BodyFamily = "Inter";
    public const string ProseFamily = "Crimson Pro";
    public const string MonoFamily = "JetBrains Mono";

    public const double TrackingWordmark = 0.14;
    public const double TrackingLabel = 0.34;
    public const double TrackingDisplay = 0.02;

    public const double SizeCaption = 11;
    public const double SizeBody = 13;
    public const double SizeSubtitle = 15;
    public const double SizeTitle = 20;
    public const double SizeDisplay = 28;

    // -- Shape ------------------------------------------------------------------------------

    public const double RadiusSm = 6;
    public const double RadiusMd = 10;
    public const double RadiusLg = 16;
    public const double RadiusPill = 999;
    public const double StrokeThickness = 1;

    public const double FlyoutRadius = 12;
    public const double FlyoutWidth = 368;
    public const double FlyoutMaxHeight = 620;
    public const double FlyoutMargin = 12;
    public const double TileHeight = 52;

    // -- Motion -----------------------------------------------------------------------------

    public const double FlyoutOpenMs = 220;
    public const double FlyoutCloseMs = 140;
    public const double HoverMs = 120;
    public const double PressMs = 70;
    public const double PageTransitionMs = 240;
    public const double StaggerStepMs = 24;
    public const int StaggerMaxItems = 12;

    /// <summary>The brand's <c>--ease</c>.</summary>
    public const string StandardEasing = "cubic-bezier(0.2, 0.7, 0.3, 1)";

    /// <summary>The brand's <c>--spring</c>. Used for the flyout so it settles rather than stops.</summary>
    public const string FlyoutEasing = "cubic-bezier(0.3, 1.5, 0.4, 1)";

    public const double FlyoutTravel = 14;
    public const double FlyoutScaleFrom = 0.97;

    // -- Backdrop ---------------------------------------------------------------------------

    public const double TintOpacity = 0.86;
    public const double FlyoutOpacity = 1.0;

    /// <summary>
    /// Approximates the brand's gilded panel shadow, <c>0 26px 60px -28px rgba(0,0,0,0.85)</c>.
    /// </summary>
    /// <remarks>
    /// CSS shadows take a spread; WPF's <c>DropShadowEffect</c> does not, so the -28px inset spread
    /// is absorbed into a shorter blur and depth. The two inset strokes that make a surface read as
    /// gilded rather than merely dark are drawn separately, as real borders, by the panel control.
    /// </remarks>
    public const double ShadowBlur = 34;
    public const double ShadowOpacity = 0.85;
    public const double ShadowDepth = 10;

    /// <summary>Inner lit edge — the brand's <c>inset 0 1px 0 rgba(255,233,168,0.08)</c>.</summary>
    public const string PanelInnerHighlight = "rgba(255, 233, 168, 0.08)";

    /// <summary>Gilding stroke — the brand's <c>inset 0 0 0 1px rgba(212,175,55,0.10)</c>.</summary>
    public const string PanelGildStroke = "rgba(212, 175, 55, 0.10)";

    // -- Buttons ----------------------------------------------------------------------------

    public const double ButtonHoverLift = 1;
    public const double ButtonPressScale = 0.985;
    public const double ButtonPadding = 10;

    // -- Ornament ---------------------------------------------------------------------------

    public const double FilmGrainOpacity = 0.035;
}
