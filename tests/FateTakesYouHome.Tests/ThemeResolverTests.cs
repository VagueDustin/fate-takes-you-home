// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using FateTakesYouHome.Theming.Loading;
using FateTakesYouHome.Theming.Model;
using Xunit;

namespace FateTakesYouHome.Tests;

public sealed class ThemeResolverTests
{
    private static Func<string, ThemeDocument?> Lookup(params ThemeDocument[] documents)
    {
        var index = documents.ToDictionary(d => d.Id!, StringComparer.OrdinalIgnoreCase);
        return id => index.TryGetValue(id, out ThemeDocument? found) ? found : null;
    }

    [Fact]
    public void AnEmptyDocumentResolvesToTheCompiledBaseline()
    {
        Theme theme = ThemeResolver.Resolve(new ThemeDocument { Id = "bare" });

        Assert.Equal(ColorParser.Parse(ThemeDefaults.AccentDefault), theme.Colors.AccentDefault);
        Assert.Equal(ThemeDefaults.FlyoutWidth, theme.Shape.FlyoutWidth);
        Assert.Equal(OrnamentTier.Charted, theme.Tier);
    }

    /// <summary>
    /// A theme is a patch: what it omits it inherits, what it states it overrides.
    /// </summary>
    [Fact]
    public void ChildValuesOverrideTheParentAndOmissionsInherit()
    {
        var parent = new ThemeDocument
        {
            Id = "parent",
            Colors = new ThemeColorsDocument { AccentDefault = "#112233", TextPrimary = "#EEEEEE" },
            Shape = new ThemeShapeDocument { RadiusMd = 20 },
        };

        var child = new ThemeDocument
        {
            Id = "child",
            BasedOn = "parent",
            Colors = new ThemeColorsDocument { AccentDefault = "#445566" },
        };

        Theme theme = ThemeResolver.Resolve(child, Lookup(parent, child));

        Assert.Equal(ColorParser.Parse("#445566"), theme.Colors.AccentDefault);
        Assert.Equal(ColorParser.Parse("#EEEEEE"), theme.Colors.TextPrimary);
        Assert.Equal(20, theme.Shape.RadiusMd);
    }

    [Fact]
    public void InheritanceFollowsAChainMoreThanOneDeep()
    {
        var root = new ThemeDocument
        {
            Id = "root",
            Colors = new ThemeColorsDocument { SurfaceBase = "#010203" },
        };

        var middle = new ThemeDocument
        {
            Id = "middle",
            BasedOn = "root",
            Colors = new ThemeColorsDocument { AccentDefault = "#0A0B0C" },
        };

        var leaf = new ThemeDocument { Id = "leaf", BasedOn = "middle" };

        Theme theme = ThemeResolver.Resolve(leaf, Lookup(root, middle, leaf));

        Assert.Equal(ColorParser.Parse("#010203"), theme.Colors.SurfaceBase);
        Assert.Equal(ColorParser.Parse("#0A0B0C"), theme.Colors.AccentDefault);
        Assert.Equal(["middle", "root"], theme.InheritanceChain);
    }

    /// <summary>
    /// A cycle must terminate rather than spin to the depth limit and produce nonsense.
    /// </summary>
    /// <remarks>
    /// Two hand-edited files that point at each other is an easy mistake to make, and the failure
    /// mode without this is an application that hangs on startup.
    /// </remarks>
    [Fact]
    public void ACycleIsBrokenRatherThanFollowed()
    {
        var a = new ThemeDocument
        {
            Id = "a",
            BasedOn = "b",
            Colors = new ThemeColorsDocument { AccentDefault = "#AAAAAA" },
        };

        var b = new ThemeDocument
        {
            Id = "b",
            BasedOn = "a",
            Colors = new ThemeColorsDocument { TextPrimary = "#BBBBBB" },
        };

        Theme theme = ThemeResolver.Resolve(a, Lookup(a, b));

        Assert.Equal(ColorParser.Parse("#AAAAAA"), theme.Colors.AccentDefault);
        Assert.Equal(ColorParser.Parse("#BBBBBB"), theme.Colors.TextPrimary);
    }

    [Fact]
    public void AMissingParentStopsTheChainWithoutFailing()
    {
        var orphan = new ThemeDocument
        {
            Id = "orphan",
            BasedOn = "nobody",
            Colors = new ThemeColorsDocument { AccentDefault = "#123456" },
        };

        Theme theme = ThemeResolver.Resolve(orphan, Lookup(orphan));

        Assert.Equal(ColorParser.Parse("#123456"), theme.Colors.AccentDefault);
        Assert.Equal(ColorParser.Parse(ThemeDefaults.TextPrimary), theme.Colors.TextPrimary);
    }

    // ------------------------------------------------------------------ tier defaults

    [Theory]
    [InlineData(OrnamentTier.Ceremonial, true, true, 5)]
    [InlineData(OrnamentTier.Charted, true, false, 3)]
    [InlineData(OrnamentTier.Utility, false, false, 1)]
    public void TheTierSetsTheOrnamentDefaults(
        OrnamentTier tier, bool brackets, bool dividers, int maxAnimations)
    {
        Theme theme = ThemeResolver.Resolve(new ThemeDocument { Id = "t", Tier = tier });

        Assert.Equal(brackets, theme.Ornament.CornerBrackets);
        Assert.Equal(dividers, theme.Ornament.OrnateDividers);
        Assert.Equal(maxAnimations, theme.Ornament.MaxConcurrentAnimations);
    }

    [Fact]
    public void AnExplicitOrnamentSwitchBeatsTheTierDefault()
    {
        var document = new ThemeDocument
        {
            Id = "t",
            Tier = OrnamentTier.Utility,
            Ornament = new ThemeOrnamentDocument { CornerBrackets = true },
        };

        Theme theme = ThemeResolver.Resolve(document);

        Assert.True(theme.Ornament.CornerBrackets);
        Assert.False(theme.Ornament.FilmGrain);
    }

    /// <summary>
    /// Declaring a tier resets the ornament switches rather than inheriting the parent's.
    /// </summary>
    /// <remarks>
    /// This is the rule that stops a deliberately plain child theme inheriting its parent's ornate
    /// flags. It was found the hard way: the light Daybreak theme, tier utility, was rendering a
    /// star field because its FATE parent had switched one on explicitly.
    /// </remarks>
    [Fact]
    public void DeclaringATierDiscardsAnAncestorsOrnamentSwitches()
    {
        var parent = new ThemeDocument
        {
            Id = "ornate",
            Tier = OrnamentTier.Ceremonial,
            Ornament = new ThemeOrnamentDocument
            {
                Starfield = true,
                FilmGrain = true,
                CornerBrackets = true,
            },
        };

        var child = new ThemeDocument
        {
            Id = "plain",
            BasedOn = "ornate",
            Tier = OrnamentTier.Utility,
        };

        Theme theme = ThemeResolver.Resolve(child, Lookup(parent, child));

        Assert.False(theme.Ornament.Starfield);
        Assert.False(theme.Ornament.FilmGrain);
        Assert.False(theme.Ornament.CornerBrackets);
        Assert.Equal(OrnamentTier.Utility, theme.Tier);
    }

    /// <summary>A theme's own switches survive its own tier reset.</summary>
    [Fact]
    public void ATierResetKeepsSwitchesTheThemeSetsItself()
    {
        var parent = new ThemeDocument
        {
            Id = "ornate",
            Tier = OrnamentTier.Ceremonial,
            Ornament = new ThemeOrnamentDocument { Starfield = true, FilmGrain = true },
        };

        var child = new ThemeDocument
        {
            Id = "mostly-plain",
            BasedOn = "ornate",
            Tier = OrnamentTier.Utility,
            Ornament = new ThemeOrnamentDocument { CornerBrackets = true },
        };

        Theme theme = ThemeResolver.Resolve(child, Lookup(parent, child));

        Assert.True(theme.Ornament.CornerBrackets);
        Assert.False(theme.Ornament.Starfield);
        Assert.False(theme.Ornament.FilmGrain);
    }

    /// <summary>Without its own tier, a theme inherits ornament as normal.</summary>
    [Fact]
    public void AThemeWithNoTierStillInheritsOrnament()
    {
        var parent = new ThemeDocument
        {
            Id = "ornate",
            Tier = OrnamentTier.Ceremonial,
            Ornament = new ThemeOrnamentDocument { Starfield = true },
        };

        var child = new ThemeDocument { Id = "follower", BasedOn = "ornate" };

        Theme theme = ThemeResolver.Resolve(child, Lookup(parent, child));

        Assert.True(theme.Ornament.Starfield);
        Assert.Equal(OrnamentTier.Ceremonial, theme.Tier);
    }

    [Theory]
    [InlineData(OrnamentTier.Ceremonial, true)]
    [InlineData(OrnamentTier.Charted, true)]
    [InlineData(OrnamentTier.Utility, false)]
    public void TheStarfieldFollowsTheTier(OrnamentTier tier, bool expected)
    {
        Theme theme = ThemeResolver.Resolve(new ThemeDocument { Id = "t", Tier = tier });

        Assert.Equal(expected, theme.Ornament.Starfield);
    }

    // ------------------------------------------------------------------ depth wash

    /// <summary>
    /// The wash is replaced wholesale, never merged layer by layer.
    /// </summary>
    /// <remarks>
    /// Merging two gradient stacks of different lengths produces a stack that is neither, and the
    /// result is impossible to reason about from the file.
    /// </remarks>
    [Fact]
    public void ADeclaredDepthWashReplacesTheInheritedOne()
    {
        var parent = new ThemeDocument
        {
            Id = "parent",
            Colors = new ThemeColorsDocument
            {
                DepthWash =
                [
                    new RadialWashDocument { Color = "#111111" },
                    new RadialWashDocument { Color = "#222222" },
                    new RadialWashDocument { Color = "#333333" },
                ],
            },
        };

        var child = new ThemeDocument
        {
            Id = "child",
            BasedOn = "parent",
            Colors = new ThemeColorsDocument
            {
                DepthWash = [new RadialWashDocument { Color = "#444444" }],
            },
        };

        Theme theme = ThemeResolver.Resolve(child, Lookup(parent, child));

        Assert.Single(theme.Colors.DepthWash);
        Assert.Equal(ColorParser.Parse("#444444"), theme.Colors.DepthWash[0].Color);
    }

    [Fact]
    public void AnEmptyDepthWashArrayMeansAFlatFill()
    {
        var document = new ThemeDocument
        {
            Id = "flat",
            Colors = new ThemeColorsDocument { DepthWash = [] },
        };

        Theme theme = ThemeResolver.Resolve(document);

        Assert.Empty(theme.Colors.DepthWash);
    }

    // ------------------------------------------------------------------ motion

    [Fact]
    public void SpeedScaleMultipliesEveryDuration()
    {
        var document = new ThemeDocument
        {
            Id = "fast",
            Motion = new ThemeMotionDocument { SpeedScale = 0.5, FlyoutOpenMs = 200 },
        };

        Theme theme = ThemeResolver.Resolve(document);

        Assert.Equal(100, theme.Motion.FlyoutOpen.TotalMilliseconds);
        Assert.Equal(ThemeDefaults.HoverMs * 0.5, theme.Motion.Hover.TotalMilliseconds);
    }

    [Theory]
    [InlineData(0.001, 0.1)]
    [InlineData(99.0, 4.0)]
    public void SpeedScaleIsClampedToSomethingUsable(double declared, double expected)
    {
        var document = new ThemeDocument
        {
            Id = "t",
            Motion = new ThemeMotionDocument { SpeedScale = declared },
        };

        Assert.Equal(expected, ThemeResolver.Resolve(document).Motion.SpeedScale);
    }

    // ------------------------------------------------------------------ easing

    [Theory]
    [InlineData("fate")]
    [InlineData("standard")]
    [InlineData("ease")]
    public void KnownEasingNamesResolveToTheBrandCurve(string name)
    {
        Assert.Equal(EasingSpec.FateOut, ThemeResolver.ParseEasing(name));
    }

    [Fact]
    public void SpringResolvesToTheOvershootCurve()
    {
        Assert.Equal(EasingSpec.FateSpring, ThemeResolver.ParseEasing("fate-spring"));
    }

    [Fact]
    public void ALiteralCubicBezierIsParsed()
    {
        EasingSpec spec = ThemeResolver.ParseEasing("cubic-bezier(0.1, 0.2, 0.3, 0.4)");

        Assert.Equal(0.1, spec.X1, 4);
        Assert.Equal(0.2, spec.Y1, 4);
        Assert.Equal(0.3, spec.X2, 4);
        Assert.Equal(0.4, spec.Y2, 4);
    }

    /// <summary>X control points outside 0–1 make the curve non-monotonic in time.</summary>
    [Fact]
    public void EasingXControlPointsAreClampedButYIsNot()
    {
        EasingSpec spec = ThemeResolver.ParseEasing("cubic-bezier(-2, 1.8, 5, -0.6)");

        Assert.Equal(0, spec.X1);
        Assert.Equal(1, spec.X2);
        Assert.Equal(1.8, spec.Y1, 4);
        Assert.Equal(-0.6, spec.Y2, 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wobbly")]
    [InlineData("cubic-bezier(1, 2)")]
    [InlineData("cubic-bezier(a, b, c, d)")]
    public void AnUnparseableEasingFallsBackRatherThanThrowing(string input)
    {
        Assert.Equal(EasingSpec.FateOut, ThemeResolver.ParseEasing(input));
    }
}
