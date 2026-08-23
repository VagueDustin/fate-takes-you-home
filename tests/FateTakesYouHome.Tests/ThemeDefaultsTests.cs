using System.IO;
using System.Windows.Media;
using FateTakesYouHome.Theming.Loading;
using FateTakesYouHome.Theming.Model;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// Holds the compiled FATE baseline and the shipped <c>fate.json</c> in agreement.
/// </summary>
/// <remarks>
/// The two exist for different reasons — the JSON is the user-visible, editable copy; the
/// constants are the fallback that guarantees the application is never themeless — and nothing but
/// this test stops them drifting apart. A drift would be invisible until somebody deleted their
/// theme file and got a subtly different FATE back.
/// </remarks>
public sealed class ThemeDefaultsTests
{
    private static ThemeDocument LoadShippedFate()
    {
        string path = TestPaths.Theme("fate.json");
        ThemeLoadResult result = ThemeLoader.Load(path);

        Assert.True(
            result.Succeeded,
            "themes/fate.json did not load: "
            + string.Join("; ", result.Validation.Diagnostics.Select(d => d.ToString())));

        return result.Document!;
    }

    /// <summary>Resolves the shipped file with no ancestry, so only its own values are in play.</summary>
    private static Theme ResolveShippedFate() => ThemeResolver.Resolve(LoadShippedFate());

    [Fact]
    public void ShippedFateFileMatchesTheCompiledColours()
    {
        Theme theme = ResolveShippedFate();
        ThemeColors c = theme.Colors;

        Assert.Equal(Parse(ThemeDefaults.SurfaceBase), c.SurfaceBase);
        Assert.Equal(Parse(ThemeDefaults.SurfaceRaised), c.SurfaceRaised);
        Assert.Equal(Parse(ThemeDefaults.SurfaceOverlay), c.SurfaceOverlay);
        Assert.Equal(Parse(ThemeDefaults.SurfaceSunken), c.SurfaceSunken);
        Assert.Equal(Parse(ThemeDefaults.SurfaceHighest), c.SurfaceHighest);

        Assert.Equal(Parse(ThemeDefaults.BorderSubtle), c.BorderSubtle);
        Assert.Equal(Parse(ThemeDefaults.BorderDefault), c.BorderDefault);
        Assert.Equal(Parse(ThemeDefaults.BorderEmphasis), c.BorderEmphasis);

        Assert.Equal(Parse(ThemeDefaults.TextPrimary), c.TextPrimary);
        Assert.Equal(Parse(ThemeDefaults.TextMuted), c.TextMuted);
        Assert.Equal(Parse(ThemeDefaults.TextFaint), c.TextFaint);
        Assert.Equal(Parse(ThemeDefaults.TextInverse), c.TextInverse);
        Assert.Equal(Parse(ThemeDefaults.TextAccent), c.TextAccent);

        Assert.Equal(Parse(ThemeDefaults.AccentDefault), c.AccentDefault);
        Assert.Equal(Parse(ThemeDefaults.AccentHover), c.AccentHover);
        Assert.Equal(Parse(ThemeDefaults.AccentPressed), c.AccentPressed);
        Assert.Equal(Parse(ThemeDefaults.AccentSubtle), c.AccentSubtle);
        Assert.Equal(Parse(ThemeDefaults.AccentGlow), c.AccentGlow);

        Assert.Equal(Parse(ThemeDefaults.StatusLive), c.StatusLive);
        Assert.Equal(Parse(ThemeDefaults.StatusSuccess), c.StatusSuccess);
        Assert.Equal(Parse(ThemeDefaults.StatusWarning), c.StatusWarning);
        Assert.Equal(Parse(ThemeDefaults.StatusDanger), c.StatusDanger);
        Assert.Equal(Parse(ThemeDefaults.StatusInfo), c.StatusInfo);
    }

    [Fact]
    public void ShippedFateFileMatchesTheCompiledMetrics()
    {
        Theme theme = ResolveShippedFate();

        Assert.Equal(ThemeDefaults.RadiusSm, theme.Shape.RadiusSm);
        Assert.Equal(ThemeDefaults.RadiusMd, theme.Shape.RadiusMd);
        Assert.Equal(ThemeDefaults.RadiusLg, theme.Shape.RadiusLg);
        Assert.Equal(ThemeDefaults.FlyoutRadius, theme.Shape.FlyoutRadius);
        Assert.Equal(ThemeDefaults.FlyoutWidth, theme.Shape.FlyoutWidth);
        Assert.Equal(ThemeDefaults.FlyoutMaxHeight, theme.Shape.FlyoutMaxHeight);
        Assert.Equal(ThemeDefaults.FlyoutMargin, theme.Shape.FlyoutMargin);
        Assert.Equal(ThemeDefaults.TileHeight, theme.Shape.TileHeight);

        Assert.Equal(ThemeDefaults.SizeBody, theme.Typography.SizeBody);
        Assert.Equal(ThemeDefaults.SizeTitle, theme.Typography.SizeTitle);
        Assert.Equal(ThemeDefaults.TrackingLabel, theme.Typography.TrackingLabel);
        Assert.Equal(ThemeDefaults.TrackingWordmark, theme.Typography.TrackingWordmark);

        Assert.Equal(ThemeDefaults.DisplayFamily, theme.Typography.DisplayFamily);
        Assert.Equal(ThemeDefaults.BodyFamily, theme.Typography.BodyFamily);
        Assert.Equal(ThemeDefaults.ProseFamily, theme.Typography.ProseFamily);
    }

    [Fact]
    public void ShippedFateFileMatchesTheCompiledMotion()
    {
        Theme theme = ResolveShippedFate();

        Assert.Equal(ThemeDefaults.FlyoutOpenMs, theme.Motion.FlyoutOpen.TotalMilliseconds);
        Assert.Equal(ThemeDefaults.FlyoutCloseMs, theme.Motion.FlyoutClose.TotalMilliseconds);
        Assert.Equal(ThemeDefaults.HoverMs, theme.Motion.Hover.TotalMilliseconds);
        Assert.Equal(ThemeDefaults.PressMs, theme.Motion.Press.TotalMilliseconds);
        Assert.Equal(ThemeDefaults.StaggerStepMs, theme.Motion.StaggerStep.TotalMilliseconds);
        Assert.Equal(ThemeDefaults.FlyoutTravel, theme.Motion.FlyoutTravel);
        Assert.Equal(ThemeDefaults.FlyoutScaleFrom, theme.Motion.FlyoutScaleFrom);

        Assert.Equal(ThemeResolver.ParseEasing(ThemeDefaults.FlyoutEasing), theme.Motion.FlyoutEasing);
        Assert.Equal(ThemeResolver.ParseEasing(ThemeDefaults.StandardEasing), theme.Motion.StandardEasing);
    }

    [Fact]
    public void ShippedFateFileMatchesTheCompiledDepthWash()
    {
        Theme theme = ResolveShippedFate();

        Assert.Equal(ThemeDefaults.DepthWash.Length, theme.Colors.DepthWash.Count);

        for (int i = 0; i < ThemeDefaults.DepthWash.Length; i++)
        {
            (double x, double y, double rx, double ry, string colour, double falloff) =
                ThemeDefaults.DepthWash[i];

            RadialWash actual = theme.Colors.DepthWash[i];

            Assert.Equal(x, actual.CenterX, 4);
            Assert.Equal(y, actual.CenterY, 4);
            Assert.Equal(rx, actual.RadiusX, 4);
            Assert.Equal(ry, actual.RadiusY, 4);
            Assert.Equal(falloff, actual.Falloff, 4);
            Assert.Equal(Parse(colour), actual.Color);
        }
    }

    /// <summary>
    /// The FATE theme must pass its own validator.
    /// </summary>
    /// <remarks>
    /// If the house theme cannot clear the contrast and brand rules the editor enforces, then
    /// either the theme is wrong or the rules are — and either way somebody needs to know.
    /// </remarks>
    [Fact]
    public void ShippedFateFileHasNoValidationErrors()
    {
        ThemeDocument document = LoadShippedFate();

        ThemeValidationResult structural = ThemeValidator.ValidateDocument(document);
        Assert.True(
            structural.IsValid,
            "Structural errors: " + string.Join("; ", structural.Errors.Select(e => e.ToString())));

        ThemeValidationResult resolved = ThemeValidator.ValidateResolved(ThemeResolver.Resolve(document));
        Assert.True(
            resolved.IsValid,
            "Resolved errors: " + string.Join("; ", resolved.Errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// Every theme that ships must load, resolve and validate.
    /// </summary>
    /// <remarks>
    /// Themes are data, so a typo in one is not a compile error. This is the only thing that
    /// catches a shipped theme with a malformed colour before a user selects it.
    /// </remarks>
    [Theory]
    [InlineData("fate.json")]
    [InlineData("fate-ceremonial.json")]
    [InlineData("fate-utility.json")]
    [InlineData("midnight.json")]
    [InlineData("daybreak.json")]
    [InlineData("mono.json")]
    public void EveryShippedThemeLoadsAndResolves(string fileName)
    {
        ThemeLoadResult result = ThemeLoader.Load(TestPaths.Theme(fileName));

        Assert.True(
            result.Succeeded,
            $"{fileName}: " + string.Join("; ", result.Validation.Errors.Select(e => e.ToString())));

        // Shipped themes inherit from fate, so resolution needs a lookup that can find it.
        Theme theme = ThemeResolver.Resolve(result.Document!, LookupShipped);

        Assert.NotNull(theme.Colors);
        Assert.False(string.IsNullOrWhiteSpace(theme.Name));

        ThemeValidationResult diagnostics = ThemeValidator.ValidateResolved(theme);
        Assert.True(
            diagnostics.IsValid,
            $"{fileName}: " + string.Join("; ", diagnostics.Errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// The shipped themes must not be a set of near-duplicates.
    /// </summary>
    /// <remarks>
    /// A theme picker where three entries look the same is a picker with one entry and two
    /// mistakes. This checks the accent actually differs between the distinct families.
    /// </remarks>
    [Fact]
    public void ShippedThemesAreVisiblyDistinct()
    {
        Color fate = ResolveShipped("fate.json").Colors.AccentDefault;
        Color midnight = ResolveShipped("midnight.json").Colors.AccentDefault;
        Color mono = ResolveShipped("mono.json").Colors.AccentDefault;
        Color daybreak = ResolveShipped("daybreak.json").Colors.SurfaceBase;

        Assert.NotEqual(fate, midnight);
        Assert.NotEqual(fate, mono);
        Assert.NotEqual(midnight, mono);

        // Daybreak is the only light theme; its base surface must actually be light.
        Assert.True(
            ColorParser.RelativeLuminance(daybreak) > 0.5,
            "Daybreak claims to be a light theme but its base surface is dark.");
    }

    private static Theme ResolveShipped(string fileName)
    {
        ThemeDocument document = ThemeLoader.Load(TestPaths.Theme(fileName)).Document!;
        return ThemeResolver.Resolve(document, LookupShipped);
    }

    private static ThemeDocument? LookupShipped(string id)
    {
        string path = TestPaths.Theme(id + ".json");
        return File.Exists(path) ? ThemeLoader.Load(path).Document : null;
    }

    private static Color Parse(string value) => ColorParser.Parse(value);
}
