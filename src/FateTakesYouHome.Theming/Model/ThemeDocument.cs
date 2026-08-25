// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace FateTakesYouHome.Theming.Model;

/// <summary>
/// The on-disk shape of a theme file.
/// </summary>
/// <remarks>
/// Every value is nullable on purpose. A theme file is a <em>patch</em>: it declares only what it
/// changes, and everything it omits is inherited from its <see cref="BasedOn"/> parent, and
/// ultimately from the built-in defaults. That is what lets somebody write a six-line theme that
/// only swaps the accent colour.
/// </remarks>
public sealed class ThemeDocument
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>Stable identifier, kebab-case. Must be unique across all loaded themes.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Display name shown in the theme picker.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    /// <summary>
    /// The id of a theme to inherit from. Chains are followed up to a depth limit; cycles are
    /// rejected at load time.
    /// </summary>
    [JsonPropertyName("basedOn")]
    public string? BasedOn { get; set; }

    /// <summary>
    /// Whether this is a dark or light theme. Drives the window title bar, the caption buttons and
    /// the default backdrop tint — things Windows must be told about explicitly.
    /// </summary>
    [JsonPropertyName("appearance")]
    public ThemeAppearance? Appearance { get; set; }

    /// <summary>Ornament tier. Sets the defaults for everything in <see cref="Ornament"/>.</summary>
    [JsonPropertyName("tier")]
    public OrnamentTier? Tier { get; set; }

    [JsonPropertyName("colors")]
    public ThemeColorsDocument? Colors { get; set; }

    [JsonPropertyName("typography")]
    public ThemeTypographyDocument? Typography { get; set; }

    [JsonPropertyName("shape")]
    public ThemeShapeDocument? Shape { get; set; }

    [JsonPropertyName("motion")]
    public ThemeMotionDocument? Motion { get; set; }

    [JsonPropertyName("ornament")]
    public ThemeOrnamentDocument? Ornament { get; set; }

    [JsonPropertyName("buttons")]
    public ThemeButtonsDocument? Buttons { get; set; }

    [JsonPropertyName("backdrop")]
    public ThemeBackdropDocument? Backdrop { get; set; }

    /// <summary>
    /// Set by the loader, not by the file. True for themes shipped inside the application, which
    /// are read-only in the editor and are restored if deleted.
    /// </summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; set; }

    /// <summary>Set by the loader. Absolute path this document was read from, when it came from disk.</summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }
}

/// <summary>Whether a theme reads as dark or light to the operating system.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThemeAppearance>))]
public enum ThemeAppearance
{
    Dark,
    Light,
}

/// <summary>
/// The ornament tier, as defined by the VagueDustin brand contract.
/// </summary>
/// <remarks>
/// The tier is not decoration on top of the theme — it is what lets one design language serve both
/// a ceremonial game portal and a restrained utility panel. Picking a tier sets sensible defaults
/// for every individual ornament switch; the switches then allow deliberate exceptions.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<OrnamentTier>))]
public enum OrnamentTier
{
    /// <summary>Gold as light. Gradient fills, ornate dividers, ambient motion. Up to five animations.</summary>
    Ceremonial,

    /// <summary>Gold as engraving. Brackets and grain, no ornate dividers. Up to three animations.</summary>
    Charted,

    /// <summary>Gold as flat material. No brackets, grain or glass. One animation at a time.</summary>
    Utility,
}

/// <summary>Semantic colour roles. Product code consumes these, never raw ramp values.</summary>
public sealed class ThemeColorsDocument
{
    [JsonPropertyName("surfaceBase")] public string? SurfaceBase { get; set; }
    [JsonPropertyName("surfaceRaised")] public string? SurfaceRaised { get; set; }
    [JsonPropertyName("surfaceOverlay")] public string? SurfaceOverlay { get; set; }
    [JsonPropertyName("surfaceSunken")] public string? SurfaceSunken { get; set; }
    [JsonPropertyName("surfaceHighest")] public string? SurfaceHighest { get; set; }

    [JsonPropertyName("borderSubtle")] public string? BorderSubtle { get; set; }
    [JsonPropertyName("borderDefault")] public string? BorderDefault { get; set; }
    [JsonPropertyName("borderEmphasis")] public string? BorderEmphasis { get; set; }

    [JsonPropertyName("textPrimary")] public string? TextPrimary { get; set; }
    [JsonPropertyName("textMuted")] public string? TextMuted { get; set; }
    [JsonPropertyName("textFaint")] public string? TextFaint { get; set; }
    [JsonPropertyName("textInverse")] public string? TextInverse { get; set; }
    [JsonPropertyName("textAccent")] public string? TextAccent { get; set; }

    [JsonPropertyName("accentDefault")] public string? AccentDefault { get; set; }
    [JsonPropertyName("accentHover")] public string? AccentHover { get; set; }
    [JsonPropertyName("accentPressed")] public string? AccentPressed { get; set; }
    [JsonPropertyName("accentSubtle")] public string? AccentSubtle { get; set; }
    [JsonPropertyName("accentGlow")] public string? AccentGlow { get; set; }

    [JsonPropertyName("statusLive")] public string? StatusLive { get; set; }
    [JsonPropertyName("statusSuccess")] public string? StatusSuccess { get; set; }
    [JsonPropertyName("statusWarning")] public string? StatusWarning { get; set; }
    [JsonPropertyName("statusDanger")] public string? StatusDanger { get; set; }
    [JsonPropertyName("statusInfo")] public string? StatusInfo { get; set; }

    /// <summary>
    /// Radial washes painted over <see cref="SurfaceBase"/>. The brand forbids a flat navy fill;
    /// this is how the depth is produced. Rendered back-to-front.
    /// </summary>
    [JsonPropertyName("depthWash")]
    public List<RadialWashDocument>? DepthWash { get; set; }
}

/// <summary>One radial gradient layer of the background depth wash.</summary>
public sealed class RadialWashDocument
{
    /// <summary>Centre X as a fraction of the surface width. May sit outside 0–1.</summary>
    [JsonPropertyName("centerX")] public double? CenterX { get; set; }

    /// <summary>Centre Y as a fraction of the surface height.</summary>
    [JsonPropertyName("centerY")] public double? CenterY { get; set; }

    /// <summary>Horizontal radius as a fraction of the surface width.</summary>
    [JsonPropertyName("radiusX")] public double? RadiusX { get; set; }

    /// <summary>Vertical radius as a fraction of the surface height.</summary>
    [JsonPropertyName("radiusY")] public double? RadiusY { get; set; }

    /// <summary>Colour at the centre, fading to fully transparent at the edge.</summary>
    [JsonPropertyName("color")] public string? Color { get; set; }

    /// <summary>Where the fade reaches zero, 0–1 along the radius.</summary>
    [JsonPropertyName("falloff")] public double? Falloff { get; set; }
}

public sealed class ThemeTypographyDocument
{
    /// <summary>Face for page titles and the wordmark.</summary>
    [JsonPropertyName("displayFamily")] public string? DisplayFamily { get; set; }

    /// <summary>Face for interface text.</summary>
    [JsonPropertyName("bodyFamily")] public string? BodyFamily { get; set; }

    /// <summary>Face for long-form prose. Only used at the ceremonial tier.</summary>
    [JsonPropertyName("proseFamily")] public string? ProseFamily { get; set; }

    [JsonPropertyName("monoFamily")] public string? MonoFamily { get; set; }

    /// <summary>Letter spacing for the wordmark, in ems.</summary>
    [JsonPropertyName("trackingWordmark")] public double? TrackingWordmark { get; set; }

    /// <summary>Letter spacing for small caps section labels, in ems.</summary>
    [JsonPropertyName("trackingLabel")] public double? TrackingLabel { get; set; }

    [JsonPropertyName("trackingDisplay")] public double? TrackingDisplay { get; set; }

    /// <summary>Multiplier applied to every font size. Accessibility escape hatch.</summary>
    [JsonPropertyName("scale")] public double? Scale { get; set; }

    [JsonPropertyName("sizeCaption")] public double? SizeCaption { get; set; }
    [JsonPropertyName("sizeBody")] public double? SizeBody { get; set; }
    [JsonPropertyName("sizeSubtitle")] public double? SizeSubtitle { get; set; }
    [JsonPropertyName("sizeTitle")] public double? SizeTitle { get; set; }
    [JsonPropertyName("sizeDisplay")] public double? SizeDisplay { get; set; }
}

public sealed class ThemeShapeDocument
{
    [JsonPropertyName("radiusSm")] public double? RadiusSm { get; set; }
    [JsonPropertyName("radiusMd")] public double? RadiusMd { get; set; }
    [JsonPropertyName("radiusLg")] public double? RadiusLg { get; set; }
    [JsonPropertyName("radiusPill")] public double? RadiusPill { get; set; }

    /// <summary>Hairline stroke width. Sub-pixel values are legitimate on high-DPI displays.</summary>
    [JsonPropertyName("strokeThickness")] public double? StrokeThickness { get; set; }

    /// <summary>Outer radius of the flyout window.</summary>
    [JsonPropertyName("flyoutRadius")] public double? FlyoutRadius { get; set; }

    /// <summary>Width of the tray flyout, in device-independent pixels.</summary>
    [JsonPropertyName("flyoutWidth")] public double? FlyoutWidth { get; set; }

    /// <summary>Ceiling on the flyout's auto-sized height before it starts scrolling.</summary>
    [JsonPropertyName("flyoutMaxHeight")] public double? FlyoutMaxHeight { get; set; }

    /// <summary>Gap between the flyout and the taskbar edge.</summary>
    [JsonPropertyName("flyoutMargin")] public double? FlyoutMargin { get; set; }

    /// <summary>Height of a single entity row in the flyout.</summary>
    [JsonPropertyName("tileHeight")] public double? TileHeight { get; set; }
}

public sealed class ThemeMotionDocument
{
    /// <summary>Master switch. False produces instant transitions everywhere.</summary>
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }

    /// <summary>
    /// Multiplier on every duration. 0.5 is twice as fast; 2.0 is half speed. Clamped to 0.1–4.
    /// </summary>
    [JsonPropertyName("speedScale")] public double? SpeedScale { get; set; }

    [JsonPropertyName("flyoutOpenMs")] public double? FlyoutOpenMs { get; set; }
    [JsonPropertyName("flyoutCloseMs")] public double? FlyoutCloseMs { get; set; }
    [JsonPropertyName("hoverMs")] public double? HoverMs { get; set; }
    [JsonPropertyName("pressMs")] public double? PressMs { get; set; }
    [JsonPropertyName("pageTransitionMs")] public double? PageTransitionMs { get; set; }

    /// <summary>Delay between consecutive items in a staggered entrance.</summary>
    [JsonPropertyName("staggerStepMs")] public double? StaggerStepMs { get; set; }

    /// <summary>How many items get a stagger delay before the rest appear together.</summary>
    [JsonPropertyName("staggerMaxItems")] public int? StaggerMaxItems { get; set; }

    /// <summary>
    /// Easing for the flyout entrance. Either a preset name or a literal
    /// <c>cubic-bezier(x1, y1, x2, y2)</c>.
    /// </summary>
    [JsonPropertyName("flyoutEasing")] public string? FlyoutEasing { get; set; }

    [JsonPropertyName("standardEasing")] public string? StandardEasing { get; set; }

    /// <summary>How far the flyout travels as it slides out of the taskbar, in pixels.</summary>
    [JsonPropertyName("flyoutTravel")] public double? FlyoutTravel { get; set; }

    /// <summary>Scale the flyout grows from. 1.0 disables the zoom component.</summary>
    [JsonPropertyName("flyoutScaleFrom")] public double? FlyoutScaleFrom { get; set; }

    /// <summary>
    /// Honour the system "show animations in Windows" setting. Leaving this on is the accessible
    /// choice and is what the brand contract requires.
    /// </summary>
    [JsonPropertyName("respectSystemReducedMotion")] public bool? RespectSystemReducedMotion { get; set; }
}

public sealed class ThemeOrnamentDocument
{
    [JsonPropertyName("cornerBrackets")] public bool? CornerBrackets { get; set; }
    [JsonPropertyName("filmGrain")] public bool? FilmGrain { get; set; }
    [JsonPropertyName("filmGrainOpacity")] public double? FilmGrainOpacity { get; set; }
    [JsonPropertyName("glassmorphism")] public bool? Glassmorphism { get; set; }
    [JsonPropertyName("ornateDividers")] public bool? OrnateDividers { get; set; }
    [JsonPropertyName("ambientMotion")] public bool? AmbientMotion { get; set; }
    [JsonPropertyName("staggerEntrances")] public bool? StaggerEntrances { get; set; }
    [JsonPropertyName("gradientDisplayFill")] public bool? GradientDisplayFill { get; set; }
    [JsonPropertyName("depthWash")] public bool? DepthWash { get; set; }

    /// <summary>
    /// Draw the star field behind the full window.
    /// </summary>
    /// <remarks>
    /// Rendered once to a bitmap and treated as an image, so it costs nothing per frame. Applies
    /// to the full window only; the tray panel stays quiet.
    /// </remarks>
    [JsonPropertyName("starfield")] public bool? Starfield { get; set; }

    /// <summary>Either <c>hairline</c> or <c>gradient</c>.</summary>
    [JsonPropertyName("panelEdge")] public PanelEdge? PanelEdge { get; set; }

    /// <summary>Maximum number of animations allowed to run at once.</summary>
    [JsonPropertyName("maxConcurrentAnimations")] public int? MaxConcurrentAnimations { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<PanelEdge>))]
public enum PanelEdge
{
    Hairline,
    Gradient,
}

public sealed class ThemeButtonsDocument
{
    [JsonPropertyName("style")] public ButtonStyle? Style { get; set; }

    /// <summary>Overrides the shape radius for buttons only. Null follows <c>shape.radiusMd</c>.</summary>
    [JsonPropertyName("radius")] public double? Radius { get; set; }

    /// <summary>Vertical offset applied on hover, in pixels. 0 disables the lift.</summary>
    [JsonPropertyName("hoverLift")] public double? HoverLift { get; set; }

    /// <summary>Scale applied while pressed. 1.0 disables the squash.</summary>
    [JsonPropertyName("pressScale")] public double? PressScale { get; set; }

    /// <summary>Draw a glow behind the control when it is active.</summary>
    [JsonPropertyName("activeGlow")] public bool? ActiveGlow { get; set; }

    /// <summary>Sweep a highlight across the surface on hover. Ceremonial and charted only.</summary>
    [JsonPropertyName("hoverSheen")] public bool? HoverSheen { get; set; }

    [JsonPropertyName("padding")] public double? Padding { get; set; }
}

/// <summary>How interactive surfaces are drawn.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ButtonStyle>))]
public enum ButtonStyle
{
    /// <summary>Recessed fill with a lit top edge. Gold reads as an engraved line.</summary>
    Engraved,

    /// <summary>Solid accent fill with a subtle sheen. Gold reads as material.</summary>
    Foil,

    /// <summary>Dark fill with an accent halo when active. Gold reads as light.</summary>
    Glow,

    /// <summary>No fill until hover. The quietest option.</summary>
    Ghost,

    /// <summary>Hairline outline, transparent fill.</summary>
    Outline,

    /// <summary>Fully rounded ends, solid fill.</summary>
    Pill,
}

public sealed class ThemeBackdropDocument
{
    [JsonPropertyName("mode")] public BackdropMode? Mode { get; set; }

    /// <summary>Opacity of the theme's own tint painted over the system backdrop, 0–1.</summary>
    [JsonPropertyName("tintOpacity")] public double? TintOpacity { get; set; }

    /// <summary>Opacity of the flyout as a whole once open, 0–1.</summary>
    [JsonPropertyName("flyoutOpacity")] public double? FlyoutOpacity { get; set; }

    /// <summary>Drop shadow blur radius behind the flyout.</summary>
    [JsonPropertyName("shadowBlur")] public double? ShadowBlur { get; set; }

    [JsonPropertyName("shadowOpacity")] public double? ShadowOpacity { get; set; }

    [JsonPropertyName("shadowDepth")] public double? ShadowDepth { get; set; }
}

/// <summary>How the window background is produced.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BackdropMode>))]
public enum BackdropMode
{
    /// <summary>
    /// The app paints everything itself into a layered window. Gives complete control over the
    /// entrance animation, and looks identical on every Windows build.
    /// </summary>
    Composited,

    /// <summary>Windows 11 acrylic behind a translucent tint. Falls back to Composited if refused.</summary>
    Acrylic,

    /// <summary>Windows 11 mica. Quieter than acrylic and cheaper to compose.</summary>
    Mica,

    /// <summary>An opaque fill. The most predictable, and the fastest on weak hardware.</summary>
    Solid,
}
