// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Windows.Media;
using FateTakesYouHome.Theming.Model;

namespace FateTakesYouHome.Theming.Loading;

/// <summary>
/// Collapses a <see cref="ThemeDocument"/> and its <c>basedOn</c> ancestry into a complete
/// <see cref="Theme"/>.
/// </summary>
/// <remarks>
/// Resolution runs root-first: the compiled defaults, then the most distant ancestor, down to the
/// document itself. The nearest non-null value for each field wins. A theme that sets only
/// <c>accentDefault</c> therefore gets a full FATE theme with one colour changed.
/// </remarks>
public static class ThemeResolver
{
    /// <summary>Guards against a pathological or hand-edited inheritance chain.</summary>
    public const int MaxInheritanceDepth = 16;

    /// <summary>
    /// Resolves <paramref name="document"/> against its ancestry.
    /// </summary>
    /// <param name="lookup">
    /// Resolves a theme id to its document. Returning null for an unknown id is fine; the resulting
    /// theme simply stops inheriting at that point and a warning is recorded.
    /// </param>
    public static Theme Resolve(
        ThemeDocument document,
        Func<string, ThemeDocument?>? lookup = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<ThemeDocument> chain = BuildChain(document, lookup, out List<string> ancestry);

        OrnamentTier tier = Pick(chain, static d => d.Tier) ?? OrnamentTier.Charted;
        ThemeOrnament tierDefaults = ThemeOrnament.ForTier(tier);

        // Declaring a tier resets the ornament switches.
        //
        // Ornament is looked up in a chain that stops at whichever document declared the tier, so
        // an ancestor's explicit switch cannot leak past it. Without this, a theme saying
        // "tier": "utility" still inherited its parent's ornate flags — which is how a light,
        // deliberately plain theme ended up with a star field painted over it. Switches the theme
        // sets itself are still honoured, because its own document is at the end of this chain.
        List<ThemeDocument> ornamentChain = ChainFromTierDeclaration(chain);

        double? radiusMd = Pick(chain, static d => d.Shape?.RadiusMd);

        return new Theme
        {
            Id = document.Id ?? "unnamed",
            Name = Pick(chain, static d => d.Name) ?? document.Id ?? "Unnamed theme",
            Author = Pick(chain, static d => d.Author) ?? "Unknown",
            Version = Pick(chain, static d => d.Version) ?? "1.0.0",
            Description = Pick(chain, static d => d.Description) ?? string.Empty,
            Homepage = Pick(chain, static d => d.Homepage),
            IsBuiltIn = document.IsBuiltIn,
            SourcePath = document.SourcePath,
            InheritanceChain = ancestry,
            Appearance = Pick(chain, static d => d.Appearance) ?? ThemeAppearance.Dark,
            Tier = tier,

            Colors = new ThemeColors
            {
                SurfaceBase = Colour(chain, static d => d.Colors?.SurfaceBase, ThemeDefaults.SurfaceBase),
                SurfaceRaised = Colour(chain, static d => d.Colors?.SurfaceRaised, ThemeDefaults.SurfaceRaised),
                SurfaceOverlay = Colour(chain, static d => d.Colors?.SurfaceOverlay, ThemeDefaults.SurfaceOverlay),
                SurfaceSunken = Colour(chain, static d => d.Colors?.SurfaceSunken, ThemeDefaults.SurfaceSunken),
                SurfaceHighest = Colour(chain, static d => d.Colors?.SurfaceHighest, ThemeDefaults.SurfaceHighest),

                BorderSubtle = Colour(chain, static d => d.Colors?.BorderSubtle, ThemeDefaults.BorderSubtle),
                BorderDefault = Colour(chain, static d => d.Colors?.BorderDefault, ThemeDefaults.BorderDefault),
                BorderEmphasis = Colour(chain, static d => d.Colors?.BorderEmphasis, ThemeDefaults.BorderEmphasis),

                TextPrimary = Colour(chain, static d => d.Colors?.TextPrimary, ThemeDefaults.TextPrimary),
                TextMuted = Colour(chain, static d => d.Colors?.TextMuted, ThemeDefaults.TextMuted),
                TextFaint = Colour(chain, static d => d.Colors?.TextFaint, ThemeDefaults.TextFaint),
                TextInverse = Colour(chain, static d => d.Colors?.TextInverse, ThemeDefaults.TextInverse),
                TextAccent = Colour(chain, static d => d.Colors?.TextAccent, ThemeDefaults.TextAccent),

                AccentDefault = Colour(chain, static d => d.Colors?.AccentDefault, ThemeDefaults.AccentDefault),
                AccentHover = Colour(chain, static d => d.Colors?.AccentHover, ThemeDefaults.AccentHover),
                AccentPressed = Colour(chain, static d => d.Colors?.AccentPressed, ThemeDefaults.AccentPressed),
                AccentSubtle = Colour(chain, static d => d.Colors?.AccentSubtle, ThemeDefaults.AccentSubtle),
                AccentGlow = Colour(chain, static d => d.Colors?.AccentGlow, ThemeDefaults.AccentGlow),

                StatusLive = Colour(chain, static d => d.Colors?.StatusLive, ThemeDefaults.StatusLive),
                StatusSuccess = Colour(chain, static d => d.Colors?.StatusSuccess, ThemeDefaults.StatusSuccess),
                StatusWarning = Colour(chain, static d => d.Colors?.StatusWarning, ThemeDefaults.StatusWarning),
                StatusDanger = Colour(chain, static d => d.Colors?.StatusDanger, ThemeDefaults.StatusDanger),
                StatusInfo = Colour(chain, static d => d.Colors?.StatusInfo, ThemeDefaults.StatusInfo),

                DepthWash = ResolveDepthWash(chain),
            },

            Typography = new ThemeTypography
            {
                DisplayFamily = Pick(chain, static d => d.Typography?.DisplayFamily) ?? ThemeDefaults.DisplayFamily,
                BodyFamily = Pick(chain, static d => d.Typography?.BodyFamily) ?? ThemeDefaults.BodyFamily,
                ProseFamily = Pick(chain, static d => d.Typography?.ProseFamily) ?? ThemeDefaults.ProseFamily,
                MonoFamily = Pick(chain, static d => d.Typography?.MonoFamily) ?? ThemeDefaults.MonoFamily,
                TrackingWordmark = Pick(chain, static d => d.Typography?.TrackingWordmark) ?? ThemeDefaults.TrackingWordmark,
                TrackingLabel = Pick(chain, static d => d.Typography?.TrackingLabel) ?? ThemeDefaults.TrackingLabel,
                TrackingDisplay = Pick(chain, static d => d.Typography?.TrackingDisplay) ?? ThemeDefaults.TrackingDisplay,
                Scale = Math.Clamp(Pick(chain, static d => d.Typography?.Scale) ?? 1.0, 0.75, 2.0),
                SizeCaption = Pick(chain, static d => d.Typography?.SizeCaption) ?? ThemeDefaults.SizeCaption,
                SizeBody = Pick(chain, static d => d.Typography?.SizeBody) ?? ThemeDefaults.SizeBody,
                SizeSubtitle = Pick(chain, static d => d.Typography?.SizeSubtitle) ?? ThemeDefaults.SizeSubtitle,
                SizeTitle = Pick(chain, static d => d.Typography?.SizeTitle) ?? ThemeDefaults.SizeTitle,
                SizeDisplay = Pick(chain, static d => d.Typography?.SizeDisplay) ?? ThemeDefaults.SizeDisplay,
            },

            Shape = new ThemeShape
            {
                RadiusSm = Pick(chain, static d => d.Shape?.RadiusSm) ?? ThemeDefaults.RadiusSm,
                RadiusMd = radiusMd ?? ThemeDefaults.RadiusMd,
                RadiusLg = Pick(chain, static d => d.Shape?.RadiusLg) ?? ThemeDefaults.RadiusLg,
                RadiusPill = Pick(chain, static d => d.Shape?.RadiusPill) ?? ThemeDefaults.RadiusPill,
                StrokeThickness = Pick(chain, static d => d.Shape?.StrokeThickness) ?? ThemeDefaults.StrokeThickness,
                FlyoutRadius = Pick(chain, static d => d.Shape?.FlyoutRadius) ?? ThemeDefaults.FlyoutRadius,
                FlyoutWidth = Math.Clamp(
                    Pick(chain, static d => d.Shape?.FlyoutWidth) ?? ThemeDefaults.FlyoutWidth, 240, 900),
                FlyoutMaxHeight = Math.Clamp(
                    Pick(chain, static d => d.Shape?.FlyoutMaxHeight) ?? ThemeDefaults.FlyoutMaxHeight, 200, 1600),
                FlyoutMargin = Pick(chain, static d => d.Shape?.FlyoutMargin) ?? ThemeDefaults.FlyoutMargin,
                TileHeight = Math.Clamp(
                    Pick(chain, static d => d.Shape?.TileHeight) ?? ThemeDefaults.TileHeight, 32, 120),
            },

            Motion = ResolveMotion(chain),

            Ornament = new ThemeOrnament
            {
                CornerBrackets = Pick(ornamentChain, static d => d.Ornament?.CornerBrackets) ?? tierDefaults.CornerBrackets,
                FilmGrain = Pick(ornamentChain, static d => d.Ornament?.FilmGrain) ?? tierDefaults.FilmGrain,
                FilmGrainOpacity = Math.Clamp(
                    Pick(ornamentChain, static d => d.Ornament?.FilmGrainOpacity) ?? ThemeDefaults.FilmGrainOpacity, 0, 0.4),
                Glassmorphism = Pick(ornamentChain, static d => d.Ornament?.Glassmorphism) ?? tierDefaults.Glassmorphism,
                OrnateDividers = Pick(ornamentChain, static d => d.Ornament?.OrnateDividers) ?? tierDefaults.OrnateDividers,
                AmbientMotion = Pick(ornamentChain, static d => d.Ornament?.AmbientMotion) ?? tierDefaults.AmbientMotion,
                StaggerEntrances = Pick(ornamentChain, static d => d.Ornament?.StaggerEntrances) ?? tierDefaults.StaggerEntrances,
                GradientDisplayFill = Pick(ornamentChain, static d => d.Ornament?.GradientDisplayFill) ?? tierDefaults.GradientDisplayFill,
                DepthWash = Pick(ornamentChain, static d => d.Ornament?.DepthWash) ?? tierDefaults.DepthWash,
                Starfield = Pick(ornamentChain, static d => d.Ornament?.Starfield) ?? tierDefaults.Starfield,
                PanelEdge = Pick(ornamentChain, static d => d.Ornament?.PanelEdge) ?? tierDefaults.PanelEdge,
                MaxConcurrentAnimations = Math.Clamp(
                    Pick(ornamentChain, static d => d.Ornament?.MaxConcurrentAnimations)
                    ?? tierDefaults.MaxConcurrentAnimations, 1, 32),
            },

            Buttons = new ThemeButtons
            {
                Style = Pick(chain, static d => d.Buttons?.Style) ?? ButtonStyle.Engraved,
                Radius = Pick(chain, static d => d.Buttons?.Radius),
                HoverLift = Pick(chain, static d => d.Buttons?.HoverLift) ?? ThemeDefaults.ButtonHoverLift,
                PressScale = Math.Clamp(
                    Pick(chain, static d => d.Buttons?.PressScale) ?? ThemeDefaults.ButtonPressScale, 0.5, 1.5),
                ActiveGlow = Pick(chain, static d => d.Buttons?.ActiveGlow) ?? (tier != OrnamentTier.Utility),
                HoverSheen = Pick(chain, static d => d.Buttons?.HoverSheen) ?? (tier == OrnamentTier.Ceremonial),
                Padding = Pick(chain, static d => d.Buttons?.Padding) ?? ThemeDefaults.ButtonPadding,
            },

            Backdrop = new ThemeBackdrop
            {
                Mode = Pick(chain, static d => d.Backdrop?.Mode) ?? BackdropMode.Composited,
                TintOpacity = Math.Clamp(
                    Pick(chain, static d => d.Backdrop?.TintOpacity) ?? ThemeDefaults.TintOpacity, 0, 1),
                FlyoutOpacity = Math.Clamp(
                    Pick(chain, static d => d.Backdrop?.FlyoutOpacity) ?? ThemeDefaults.FlyoutOpacity, 0.2, 1),
                ShadowBlur = Math.Max(0, Pick(chain, static d => d.Backdrop?.ShadowBlur) ?? ThemeDefaults.ShadowBlur),
                ShadowOpacity = Math.Clamp(
                    Pick(chain, static d => d.Backdrop?.ShadowOpacity) ?? ThemeDefaults.ShadowOpacity, 0, 1),
                ShadowDepth = Pick(chain, static d => d.Backdrop?.ShadowDepth) ?? ThemeDefaults.ShadowDepth,
            },
        };
    }

    private static ThemeMotion ResolveMotion(List<ThemeDocument> chain)
    {
        double scale = Math.Clamp(Pick(chain, static d => d.Motion?.SpeedScale) ?? 1.0, 0.1, 4.0);

        TimeSpan Duration(double? declared, double fallback) =>
            TimeSpan.FromMilliseconds(Math.Max(0, (declared ?? fallback) * scale));

        return new ThemeMotion
        {
            Enabled = Pick(chain, static d => d.Motion?.Enabled) ?? true,
            SpeedScale = scale,
            FlyoutOpen = Duration(Pick(chain, static d => d.Motion?.FlyoutOpenMs), ThemeDefaults.FlyoutOpenMs),
            FlyoutClose = Duration(Pick(chain, static d => d.Motion?.FlyoutCloseMs), ThemeDefaults.FlyoutCloseMs),
            Hover = Duration(Pick(chain, static d => d.Motion?.HoverMs), ThemeDefaults.HoverMs),
            Press = Duration(Pick(chain, static d => d.Motion?.PressMs), ThemeDefaults.PressMs),
            PageTransition = Duration(
                Pick(chain, static d => d.Motion?.PageTransitionMs), ThemeDefaults.PageTransitionMs),
            StaggerStep = Duration(Pick(chain, static d => d.Motion?.StaggerStepMs), ThemeDefaults.StaggerStepMs),
            StaggerMaxItems = Math.Clamp(
                Pick(chain, static d => d.Motion?.StaggerMaxItems) ?? ThemeDefaults.StaggerMaxItems, 0, 200),
            FlyoutEasing = ParseEasing(
                Pick(chain, static d => d.Motion?.FlyoutEasing) ?? ThemeDefaults.FlyoutEasing),
            StandardEasing = ParseEasing(
                Pick(chain, static d => d.Motion?.StandardEasing) ?? ThemeDefaults.StandardEasing),
            FlyoutTravel = Pick(chain, static d => d.Motion?.FlyoutTravel) ?? ThemeDefaults.FlyoutTravel,
            FlyoutScaleFrom = Math.Clamp(
                Pick(chain, static d => d.Motion?.FlyoutScaleFrom) ?? ThemeDefaults.FlyoutScaleFrom, 0.5, 1.5),
            RespectSystemReducedMotion =
                Pick(chain, static d => d.Motion?.RespectSystemReducedMotion) ?? true,
        };
    }

    private static IReadOnlyList<RadialWash> ResolveDepthWash(List<ThemeDocument> chain)
    {
        // The wash is replaced wholesale rather than merged. Merging gradient stacks item by item
        // produces layered nonsense the moment two themes disagree on how many layers there are.
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            List<RadialWashDocument>? declared = chain[i].Colors?.DepthWash;
            if (declared is null)
            {
                continue;
            }

            var washes = new List<RadialWash>(declared.Count);
            foreach (RadialWashDocument layer in declared)
            {
                if (!ColorParser.TryParse(layer.Color, out Color colour))
                {
                    continue;
                }

                washes.Add(new RadialWash(
                    layer.CenterX ?? 0.5,
                    layer.CenterY ?? 0.5,
                    layer.RadiusX ?? 0.8,
                    layer.RadiusY ?? 0.8,
                    colour,
                    Math.Clamp(layer.Falloff ?? 1.0, 0.01, 1.0)));
            }

            return washes;
        }

        return ThemeDefaults.DepthWash
            .Select(w => new RadialWash(w.X, w.Y, w.Rx, w.Ry, ColorParser.Parse(w.Color), w.Falloff))
            .ToArray();
    }

    /// <summary>
    /// Parses a named preset or a literal <c>cubic-bezier(...)</c>. Unrecognised input falls back to
    /// the brand's standard curve rather than throwing — a typo in a theme should not be fatal.
    /// </summary>
    public static EasingSpec ParseEasing(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EasingSpec.FateOut;
        }

        string text = value.Trim();

        switch (text.ToLowerInvariant())
        {
            case "fate" or "fate-out" or "standard" or "ease": return EasingSpec.FateOut;
            case "fate-spring" or "spring" or "overshoot": return EasingSpec.FateSpring;
            case "linear": return EasingSpec.Linear;
            case "ease-out": return EasingSpec.EaseOut;
            case "ease-in": return EasingSpec.EaseIn;
            case "ease-in-out": return EasingSpec.EaseInOut;
        }

        if (!text.StartsWith("cubic-bezier", StringComparison.OrdinalIgnoreCase))
        {
            return EasingSpec.FateOut;
        }

        int open = text.IndexOf('(');
        int close = text.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return EasingSpec.FateOut;
        }

        string[] parts = text[(open + 1)..close]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 4)
        {
            return EasingSpec.FateOut;
        }

        Span<double> points = stackalloc double[4];
        for (int i = 0; i < 4; i++)
        {
            if (!double.TryParse(
                    parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out points[i]))
            {
                return EasingSpec.FateOut;
            }
        }

        // X control points outside 0–1 make the curve non-monotonic in time, which CSS also forbids.
        return new EasingSpec(
            Math.Clamp(points[0], 0, 1),
            points[1],
            Math.Clamp(points[2], 0, 1),
            points[3]);
    }

    /// <summary>
    /// Trims the chain to start at whichever document declared the ornament tier.
    /// </summary>
    /// <remarks>
    /// The chain arrives root-first. Anything above the nearest <c>tier</c> declaration is dropped,
    /// so a theme that picks a tier gets that tier's ornament defaults rather than its ancestor's
    /// explicit choices — while still keeping any switch it sets itself, since its own document is
    /// the last entry. A theme that declares no tier inherits the whole chain as before.
    /// </remarks>
    private static List<ThemeDocument> ChainFromTierDeclaration(List<ThemeDocument> rootFirstChain)
    {
        for (int i = rootFirstChain.Count - 1; i >= 0; i--)
        {
            if (rootFirstChain[i].Tier is not null)
            {
                return rootFirstChain.GetRange(i, rootFirstChain.Count - i);
            }
        }

        return rootFirstChain;
    }

    /// <summary>
    /// Walks <c>basedOn</c> to the root, returning the chain root-first and recording the ancestry.
    /// </summary>
    private static List<ThemeDocument> BuildChain(
        ThemeDocument document,
        Func<string, ThemeDocument?>? lookup,
        out List<string> ancestry)
    {
        var chain = new List<ThemeDocument> { document };
        ancestry = [];

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.Id is { Length: > 0 } selfId)
        {
            visited.Add(selfId);
        }

        ThemeDocument current = document;

        while (lookup is not null
               && current.BasedOn is { Length: > 0 } parentId
               && chain.Count < MaxInheritanceDepth)
        {
            // A cycle would otherwise spin until the depth limit and produce a nonsense theme.
            if (!visited.Add(parentId))
            {
                break;
            }

            ThemeDocument? parent = lookup(parentId);
            if (parent is null)
            {
                break;
            }

            chain.Add(parent);
            ancestry.Add(parentId);
            current = parent;
        }

        // Root-first, so that later entries override earlier ones during Pick.
        chain.Reverse();
        return chain;
    }

    /// <summary>Returns the nearest declared value, searching from the document back to the root.</summary>
    private static T? Pick<T>(List<ThemeDocument> rootFirstChain, Func<ThemeDocument, T?> selector)
        where T : struct
    {
        for (int i = rootFirstChain.Count - 1; i >= 0; i--)
        {
            if (selector(rootFirstChain[i]) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private static T? Pick<T>(List<ThemeDocument> rootFirstChain, Func<ThemeDocument, T?> selector)
        where T : class
    {
        for (int i = rootFirstChain.Count - 1; i >= 0; i--)
        {
            if (selector(rootFirstChain[i]) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private static Color Colour(
        List<ThemeDocument> chain, Func<ThemeDocument, string?> selector, string fallback)
    {
        string? declared = Pick(chain, selector);

        if (declared is not null && ColorParser.TryParse(declared, out Color parsed))
        {
            return parsed;
        }

        return ColorParser.Parse(fallback);
    }
}
