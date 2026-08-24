namespace FateTakesYouHome.Theming.Rendering;

/// <summary>
/// The resource keys a theme publishes into the application's resource dictionary.
/// </summary>
/// <remarks>
/// XAML binds these with <c>DynamicResource</c> so that swapping a theme — or hot-reloading an
/// edited one — repaints the live UI without rebuilding a single view. Nothing in the application
/// should reference a colour, duration or radius by any other means.
/// </remarks>
public static class ThemeKeys
{
    /// <summary>The resolved <see cref="Model.Theme"/> itself, for code that needs the whole object.</summary>
    public const string Theme = "Fate.Theme";

    // -- Brushes ----------------------------------------------------------------------------

    public const string BrushSurfaceBase = "Fate.Brush.SurfaceBase";
    public const string BrushSurfaceRaised = "Fate.Brush.SurfaceRaised";
    public const string BrushSurfaceOverlay = "Fate.Brush.SurfaceOverlay";
    public const string BrushSurfaceSunken = "Fate.Brush.SurfaceSunken";
    public const string BrushSurfaceHighest = "Fate.Brush.SurfaceHighest";

    public const string BrushBorderSubtle = "Fate.Brush.BorderSubtle";
    public const string BrushBorderDefault = "Fate.Brush.BorderDefault";
    public const string BrushBorderEmphasis = "Fate.Brush.BorderEmphasis";

    public const string BrushTextPrimary = "Fate.Brush.TextPrimary";
    public const string BrushTextMuted = "Fate.Brush.TextMuted";
    public const string BrushTextFaint = "Fate.Brush.TextFaint";
    public const string BrushTextInverse = "Fate.Brush.TextInverse";
    public const string BrushTextAccent = "Fate.Brush.TextAccent";

    public const string BrushAccentDefault = "Fate.Brush.AccentDefault";
    public const string BrushAccentHover = "Fate.Brush.AccentHover";
    public const string BrushAccentPressed = "Fate.Brush.AccentPressed";
    public const string BrushAccentSubtle = "Fate.Brush.AccentSubtle";
    public const string BrushAccentGlow = "Fate.Brush.AccentGlow";

    public const string BrushStatusLive = "Fate.Brush.StatusLive";
    public const string BrushStatusSuccess = "Fate.Brush.StatusSuccess";
    public const string BrushStatusWarning = "Fate.Brush.StatusWarning";
    public const string BrushStatusDanger = "Fate.Brush.StatusDanger";
    public const string BrushStatusInfo = "Fate.Brush.StatusInfo";

    /// <summary>The base surface with the depth wash composited over it. Never a flat fill.</summary>
    public const string BrushWindowBackground = "Fate.Brush.WindowBackground";

    /// <summary>The lit top edge of a gilded panel.</summary>
    public const string BrushPanelHighlight = "Fate.Brush.PanelHighlight";

    /// <summary>The inner gilding stroke that makes a surface read as gold rather than merely dark.</summary>
    public const string BrushPanelGild = "Fate.Brush.PanelGild";

    /// <summary>Hairline or gradient, depending on the ornament tier.</summary>
    public const string BrushPanelEdge = "Fate.Brush.PanelEdge";

    /// <summary>
    /// The raised surface at partial opacity, for page panels that should let the backdrop —
    /// the starfield in particular — read through instead of walling it off.
    /// </summary>
    public const string BrushPanelWash = "Fate.Brush.PanelWash";

    /// <summary>Metallic gradient for display type. Only used when the tier allows a gradient fill.</summary>
    public const string BrushGoldGradient = "Fate.Brush.GoldGradient";

    /// <summary>Diagonal highlight swept across a control on hover.</summary>
    public const string BrushGoldSheen = "Fate.Brush.GoldSheen";

    /// <summary>Tiling monochrome noise. Null when the tier has film grain switched off.</summary>
    public const string BrushFilmGrain = "Fate.Brush.FilmGrain";

    /// <summary>The night sky behind the full window. Rendered once, then just an image.</summary>
    public const string BrushStarfield = "Fate.Brush.Starfield";

    // -- Colours ----------------------------------------------------------------------------
    // Animations interpolate Color, not Brush, so the raw values are published too.

    public const string ColorSurfaceBase = "Fate.Color.SurfaceBase";
    public const string ColorSurfaceRaised = "Fate.Color.SurfaceRaised";
    public const string ColorSurfaceOverlay = "Fate.Color.SurfaceOverlay";
    public const string ColorSurfaceHighest = "Fate.Color.SurfaceHighest";
    public const string ColorTextPrimary = "Fate.Color.TextPrimary";
    public const string ColorTextMuted = "Fate.Color.TextMuted";
    public const string ColorTextInverse = "Fate.Color.TextInverse";
    public const string ColorAccentDefault = "Fate.Color.AccentDefault";
    public const string ColorAccentHover = "Fate.Color.AccentHover";
    public const string ColorAccentPressed = "Fate.Color.AccentPressed";
    public const string ColorAccentGlow = "Fate.Color.AccentGlow";
    public const string ColorBorderDefault = "Fate.Color.BorderDefault";
    public const string ColorBorderEmphasis = "Fate.Color.BorderEmphasis";

    // -- Typography -------------------------------------------------------------------------

    public const string FontDisplay = "Fate.Font.Display";
    public const string FontBody = "Fate.Font.Body";
    public const string FontProse = "Fate.Font.Prose";
    public const string FontMono = "Fate.Font.Mono";

    public const string SizeCaption = "Fate.Size.Caption";
    public const string SizeBody = "Fate.Size.Body";
    public const string SizeSubtitle = "Fate.Size.Subtitle";
    public const string SizeTitle = "Fate.Size.Title";
    public const string SizeDisplay = "Fate.Size.Display";

    /// <summary>Letter spacing in ems. Consumed by <c>TrackedTextBlock</c>.</summary>
    public const string TrackingWordmark = "Fate.Tracking.Wordmark";

    public const string TrackingLabel = "Fate.Tracking.Label";
    public const string TrackingDisplay = "Fate.Tracking.Display";

    // -- Shape ------------------------------------------------------------------------------

    public const string RadiusSm = "Fate.Radius.Sm";
    public const string RadiusMd = "Fate.Radius.Md";
    public const string RadiusLg = "Fate.Radius.Lg";
    public const string RadiusPill = "Fate.Radius.Pill";
    public const string RadiusFlyout = "Fate.Radius.Flyout";
    public const string RadiusButton = "Fate.Radius.Button";

    public const string ThicknessStroke = "Fate.Thickness.Stroke";
    public const string ThicknessButtonPadding = "Fate.Thickness.ButtonPadding";

    // -- Metrics ----------------------------------------------------------------------------

    public const string MetricFlyoutWidth = "Fate.Metric.FlyoutWidth";
    public const string MetricFlyoutMaxHeight = "Fate.Metric.FlyoutMaxHeight";
    public const string MetricFlyoutMargin = "Fate.Metric.FlyoutMargin";
    public const string MetricTileHeight = "Fate.Metric.TileHeight";
    public const string MetricHoverLift = "Fate.Metric.HoverLift";
    public const string MetricPressScale = "Fate.Metric.PressScale";
    public const string MetricFlyoutTravel = "Fate.Metric.FlyoutTravel";
    public const string MetricFlyoutScaleFrom = "Fate.Metric.FlyoutScaleFrom";

    // -- Motion -----------------------------------------------------------------------------

    public const string DurationFlyoutOpen = "Fate.Duration.FlyoutOpen";
    public const string DurationFlyoutClose = "Fate.Duration.FlyoutClose";
    public const string DurationHover = "Fate.Duration.Hover";
    public const string DurationPress = "Fate.Duration.Press";
    public const string DurationPageTransition = "Fate.Duration.PageTransition";
    public const string DurationStaggerStep = "Fate.Duration.StaggerStep";

    public const string EasingFlyout = "Fate.Easing.Flyout";
    public const string EasingStandard = "Fate.Easing.Standard";

    public const string MotionEnabled = "Fate.Motion.Enabled";

    // -- Effects ----------------------------------------------------------------------------

    /// <summary>The gilded panel shadow, as close as a WPF drop shadow gets to the CSS original.</summary>
    public const string EffectPanelShadow = "Fate.Effect.PanelShadow";

    /// <summary>Accent halo behind an active control.</summary>
    public const string EffectAccentGlow = "Fate.Effect.AccentGlow";

    // -- Ornament flags ---------------------------------------------------------------------

    public const string OrnamentCornerBrackets = "Fate.Ornament.CornerBrackets";
    public const string OrnamentFilmGrain = "Fate.Ornament.FilmGrain";
    public const string OrnamentGlassmorphism = "Fate.Ornament.Glassmorphism";
    public const string OrnamentOrnateDividers = "Fate.Ornament.OrnateDividers";
    public const string OrnamentAmbientMotion = "Fate.Ornament.AmbientMotion";
    public const string OrnamentStaggerEntrances = "Fate.Ornament.StaggerEntrances";
    public const string OrnamentGradientDisplayFill = "Fate.Ornament.GradientDisplayFill";
    public const string OrnamentDepthWash = "Fate.Ornament.DepthWash";

    // -- Ready-to-bind visibilities ---------------------------------------------------------
    // XAML cannot run a converter over a DynamicResource, so the ornament switches are published
    // a second time as Visibility values that a template can bind to directly.

    public const string VisibilityCornerBrackets = "Fate.Visibility.CornerBrackets";
    public const string VisibilityFilmGrain = "Fate.Visibility.FilmGrain";
    public const string VisibilityOrnateDividers = "Fate.Visibility.OrnateDividers";
    public const string VisibilityGlassmorphism = "Fate.Visibility.Glassmorphism";
    public const string VisibilityStarfield = "Fate.Visibility.Starfield";

    /// <summary>Opacity the film grain overlay should be drawn at.</summary>
    public const string OpacityFilmGrain = "Fate.Opacity.FilmGrain";

    public const string ButtonActiveGlow = "Fate.Buttons.ActiveGlow";
    public const string ButtonHoverSheen = "Fate.Buttons.HoverSheen";
    public const string ButtonStyleName = "Fate.Buttons.StyleName";

    // -- Resolved button surfaces -------------------------------------------------------------
    // The theme's `buttons.style` is collapsed into concrete brushes here, so a single control
    // template serves every style instead of one template per style with a trigger to pick.

    public const string BrushButtonFace = "Fate.Brush.ButtonFace";
    public const string BrushButtonFaceHover = "Fate.Brush.ButtonFaceHover";
    public const string BrushButtonFacePressed = "Fate.Brush.ButtonFacePressed";
    public const string BrushButtonFaceActive = "Fate.Brush.ButtonFaceActive";
    public const string BrushButtonFaceDisabled = "Fate.Brush.ButtonFaceDisabled";

    public const string BrushButtonStroke = "Fate.Brush.ButtonStroke";
    public const string BrushButtonStrokeHover = "Fate.Brush.ButtonStrokeHover";
    public const string BrushButtonStrokeActive = "Fate.Brush.ButtonStrokeActive";

    public const string BrushButtonText = "Fate.Brush.ButtonText";
    public const string BrushButtonTextHover = "Fate.Brush.ButtonTextHover";
    public const string BrushButtonTextActive = "Fate.Brush.ButtonTextActive";
    public const string BrushButtonTextDisabled = "Fate.Brush.ButtonTextDisabled";

    /// <summary>Lit top edge drawn inside an engraved button. Transparent for other styles.</summary>
    public const string BrushButtonTopLight = "Fate.Brush.ButtonTopLight";
}
