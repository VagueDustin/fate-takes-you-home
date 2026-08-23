using FateTakesYouHome.Theming.Loading;
using FateTakesYouHome.Theming.Model;
using FateTakesYouHome.Theming.Rendering;
using Xunit;

namespace FateTakesYouHome.Tests;

public sealed class ThemeValidatorTests
{
    private static bool HasError(ThemeValidationResult result, string field) =>
        result.Errors.Any(d => d.Field.Contains(field, StringComparison.OrdinalIgnoreCase));

    private static bool HasWarning(ThemeValidationResult result, string field) =>
        result.Warnings.Any(d => d.Field.Contains(field, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void AMissingIdIsAnError()
    {
        ThemeValidationResult result = ThemeValidator.ValidateDocument(new ThemeDocument());

        Assert.False(result.IsValid);
        Assert.True(HasError(result, "id"));
    }

    [Theory]
    [InlineData("Midnight Brass")]
    [InlineData("midnight_brass")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    public void AnIdThatIsNotKebabCaseIsAnError(string id)
    {
        ThemeValidationResult result = ThemeValidator.ValidateDocument(new ThemeDocument { Id = id });

        Assert.True(HasError(result, "id"), $"'{id}' should have been rejected.");
    }

    [Theory]
    [InlineData("fate")]
    [InlineData("midnight-brass")]
    [InlineData("theme2")]
    public void AValidIdIsAccepted(string id)
    {
        ThemeValidationResult result = ThemeValidator.ValidateDocument(new ThemeDocument { Id = id });

        Assert.False(HasError(result, "id"));
    }

    [Fact]
    public void SelfInheritanceIsAnError()
    {
        ThemeValidationResult result = ThemeValidator.ValidateDocument(
            new ThemeDocument { Id = "loop", BasedOn = "loop" });

        Assert.True(HasError(result, "basedOn"));
    }

    [Fact]
    public void AMalformedColourIsReportedAgainstItsOwnField()
    {
        ThemeValidationResult result = ThemeValidator.ValidateDocument(new ThemeDocument
        {
            Id = "bad",
            Colors = new ThemeColorsDocument { AccentDefault = "#ZZZZZZ" },
        });

        Assert.True(HasError(result, "accentDefault"));
    }

    // ------------------------------------------------------------------ contrast

    /// <summary>
    /// Body text that cannot be read on its own surface is an error, not a warning.
    /// </summary>
    [Fact]
    public void UnreadableBodyTextIsAnError()
    {
        var document = new ThemeDocument
        {
            Id = "unreadable",
            Colors = new ThemeColorsDocument
            {
                SurfaceBase = "#101010",
                SurfaceRaised = "#141414",
                TextPrimary = "#181818",
            },
        };

        ThemeValidationResult result =
            ThemeValidator.ValidateResolved(ThemeResolver.Resolve(document));

        Assert.False(result.IsValid);
        Assert.True(HasError(result, "textPrimary"));
    }

    [Fact]
    public void TheDefaultThemePassesEveryContrastCheck()
    {
        Theme theme = ThemeResolver.Resolve(new ThemeDocument { Id = "fate" });

        ThemeValidationResult result = ThemeValidator.ValidateResolved(theme);

        Assert.True(
            result.IsValid,
            string.Join("; ", result.Errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// The accent means "you can press this". A status colour that looks the same will be misread.
    /// </summary>
    [Fact]
    public void AStatusColourIndistinguishableFromTheAccentIsWarnedAbout()
    {
        var document = new ThemeDocument
        {
            Id = "confusing",
            Colors = new ThemeColorsDocument
            {
                AccentDefault = "#D4AF37",
                StatusWarning = "#D6B139",
            },
        };

        ThemeValidationResult result =
            ThemeValidator.ValidateResolved(ThemeResolver.Resolve(document));

        Assert.True(HasWarning(result, "statusWarning"));
    }

    [Fact]
    public void ADarkThemeWithALightSurfaceIsWarnedAbout()
    {
        var document = new ThemeDocument
        {
            Id = "mismatched",
            Appearance = ThemeAppearance.Dark,
            Colors = new ThemeColorsDocument
            {
                SurfaceBase = "#FFFFFF",
                SurfaceRaised = "#F4F4F4",
                TextPrimary = "#111111",
                TextMuted = "#444444",
                TextFaint = "#555555",
            },
        };

        ThemeValidationResult result =
            ThemeValidator.ValidateResolved(ThemeResolver.Resolve(document));

        Assert.True(HasWarning(result, "appearance"));
    }

    // ------------------------------------------------------------------ tier coherence

    [Fact]
    public void CeremonialDevicesInAUtilityThemeAreWarnedAbout()
    {
        var document = new ThemeDocument
        {
            Id = "mixed",
            Tier = OrnamentTier.Utility,
            Ornament = new ThemeOrnamentDocument { AmbientMotion = true, OrnateDividers = true },
        };

        ThemeValidationResult result =
            ThemeValidator.ValidateResolved(ThemeResolver.Resolve(document));

        Assert.True(HasWarning(result, "ambientMotion"));
        Assert.True(HasWarning(result, "ornateDividers"));
    }

    [Fact]
    public void IgnoringTheSystemReducedMotionSettingIsWarnedAbout()
    {
        var document = new ThemeDocument
        {
            Id = "insistent",
            Motion = new ThemeMotionDocument { RespectSystemReducedMotion = false },
        };

        ThemeValidationResult result =
            ThemeValidator.ValidateResolved(ThemeResolver.Resolve(document));

        Assert.True(HasWarning(result, "respectSystemReducedMotion"));
    }
}

public sealed class ThemeLoaderTests
{
    [Fact]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        const string json =
            """
            {
              // A hand-written theme.
              "id": "lenient",
              "name": "Lenient",
              "colors": {
                "accentDefault": "#123456",
              },
            }
            """;

        ThemeLoadResult result = ThemeLoader.Parse(json);

        Assert.True(result.Succeeded);
        Assert.Equal("#123456", result.Document!.Colors!.AccentDefault);
    }

    [Fact]
    public void AMalformedFileReportsTheLineItFailedOn()
    {
        ThemeLoadResult result = ThemeLoader.Parse("{ \"id\": \"x\", \"name\": }");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e => e.Message.Contains("Line", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyDocumentIsRejectedWithAPlainMessage()
    {
        ThemeLoadResult result = ThemeLoader.Parse("null");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e => e.Message.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AFileWithNoIdTakesItsIdFromTheFileName()
    {
        ThemeLoadResult result = ThemeLoader.Parse(
            """{ "name": "Nameless" }""", sourcePath: @"C:\themes\Midnight-Brass.json");

        Assert.Equal("midnight-brass", result.Document!.Id);
    }

    [Fact]
    public void SerialisingAddsTheSchemaReference()
    {
        var document = new ThemeDocument { Id = "x", Name = "X" };

        string json = ThemeLoader.Serialise(document);

        Assert.Contains("$schema", json, StringComparison.Ordinal);
        Assert.Contains("theme.schema.json", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SerialisingOmitsEverythingNotDeclared()
    {
        var document = new ThemeDocument { Id = "tiny", Name = "Tiny" };

        string json = ThemeLoader.Serialise(document);

        // A theme is a patch; writing out every unset property would turn a six-line file into a
        // full snapshot that stops inheriting the moment its parent is retuned.
        Assert.DoesNotContain("\"colors\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"motion\"", json, StringComparison.Ordinal);
    }
}

public sealed class ThemeRepositoryNamingTests
{
    [Theory]
    [InlineData("Midnight Brass", "midnight-brass")]
    [InlineData("  Leading and trailing  ", "leading-and-trailing")]
    [InlineData("Odd!!!Characters", "odd-characters")]
    [InlineData("ALLCAPS", "allcaps")]
    [InlineData("", "theme")]
    [InlineData("!!!", "theme")]
    public void SanitiseProducesAValidId(string input, string expected)
    {
        Assert.Equal(expected, ThemeRepository.Sanitise(input));
    }
}

public sealed class ThemeResourceBuilderTests
{
    [Fact]
    public void EveryPublishedKeyIsPresentForTheDefaultTheme()
    {
        Theme theme = ThemeResolver.Resolve(new ThemeDocument { Id = "fate" });

        System.Windows.ResourceDictionary resources = ThemeResourceBuilder.Build(theme);

        // A missing key is not a compile error — XAML asks for it by string — so the whole set is
        // checked here. A DynamicResource that resolves to nothing renders as a silent blank.
        foreach (string key in AllKeys())
        {
            Assert.True(resources.Contains(key), $"The theme did not publish '{key}'.");
        }
    }

    [Fact]
    public void BrushesAreFrozenSoTheyCanBeSharedAndCached()
    {
        Theme theme = ThemeResolver.Resolve(new ThemeDocument { Id = "fate" });

        System.Windows.ResourceDictionary resources = ThemeResourceBuilder.Build(theme);

        foreach (object? value in resources.Values)
        {
            if (value is System.Windows.Freezable freezable)
            {
                Assert.True(freezable.IsFrozen, $"{value.GetType().Name} was left unfrozen.");
            }
        }
    }

    [Fact]
    public void DisablingTheDepthWashProducesAPlainBrush()
    {
        var document = new ThemeDocument
        {
            Id = "flat",
            Ornament = new ThemeOrnamentDocument { DepthWash = false },
        };

        System.Windows.ResourceDictionary resources =
            ThemeResourceBuilder.Build(ThemeResolver.Resolve(document));

        Assert.IsType<System.Windows.Media.SolidColorBrush>(
            resources[ThemeKeys.BrushWindowBackground]);
    }

    [Fact]
    public void TheDepthWashProducesALayeredBrush()
    {
        System.Windows.ResourceDictionary resources =
            ThemeResourceBuilder.Build(ThemeResolver.Resolve(new ThemeDocument { Id = "fate" }));

        Assert.IsType<System.Windows.Media.DrawingBrush>(
            resources[ThemeKeys.BrushWindowBackground]);
    }

    [Fact]
    public void MotionDisabledProducesZeroDurations()
    {
        var document = new ThemeDocument
        {
            Id = "still",
            Motion = new ThemeMotionDocument { Enabled = false },
        };

        System.Windows.ResourceDictionary resources =
            ThemeResourceBuilder.Build(ThemeResolver.Resolve(document));

        var duration = (System.Windows.Duration)resources[ThemeKeys.DurationFlyoutOpen];

        Assert.Equal(TimeSpan.Zero, duration.TimeSpan);
    }

    /// <summary>Each button style must resolve to a distinct resting face.</summary>
    [Theory]
    [InlineData(ButtonStyle.Engraved)]
    [InlineData(ButtonStyle.Foil)]
    [InlineData(ButtonStyle.Glow)]
    [InlineData(ButtonStyle.Ghost)]
    [InlineData(ButtonStyle.Outline)]
    [InlineData(ButtonStyle.Pill)]
    public void EveryButtonStyleResolvesToACompleteSetOfBrushes(ButtonStyle style)
    {
        var document = new ThemeDocument
        {
            Id = "b",
            Buttons = new ThemeButtonsDocument { Style = style },
        };

        System.Windows.ResourceDictionary resources =
            ThemeResourceBuilder.Build(ThemeResolver.Resolve(document));

        foreach (string key in new[]
                 {
                     ThemeKeys.BrushButtonFace,
                     ThemeKeys.BrushButtonFaceHover,
                     ThemeKeys.BrushButtonFacePressed,
                     ThemeKeys.BrushButtonFaceActive,
                     ThemeKeys.BrushButtonFaceDisabled,
                     ThemeKeys.BrushButtonStroke,
                     ThemeKeys.BrushButtonStrokeHover,
                     ThemeKeys.BrushButtonStrokeActive,
                     ThemeKeys.BrushButtonText,
                     ThemeKeys.BrushButtonTextHover,
                     ThemeKeys.BrushButtonTextActive,
                     ThemeKeys.BrushButtonTextDisabled,
                     ThemeKeys.BrushButtonTopLight,
                 })
        {
            Assert.True(resources.Contains(key), $"{style} did not publish '{key}'.");
        }
    }

    /// <summary>Reflects over ThemeKeys so a key added without a value fails here.</summary>
    private static IEnumerable<string> AllKeys() =>
        typeof(ThemeKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);
}
