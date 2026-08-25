// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows.Media;
using FateTakesYouHome.Theming.Model;
using Xunit;

namespace FateTakesYouHome.Tests;

public sealed class ColorParserTests
{
    [Theory]
    [InlineData("#D4AF37", 0xFF, 0xD4, 0xAF, 0x37)]
    [InlineData("#d4af37", 0xFF, 0xD4, 0xAF, 0x37)]
    [InlineData("#FA0", 0xFF, 0xFF, 0xAA, 0x00)]
    public void ParsesHexWithoutAlpha(string input, byte a, byte r, byte g, byte b)
    {
        Color colour = ColorParser.Parse(input);

        Assert.Equal(Color.FromArgb(a, r, g, b), colour);
    }

    /// <summary>
    /// Eight-digit hex is read in CSS order, alpha last.
    /// </summary>
    /// <remarks>
    /// WPF's own parser reads <c>#AARRGGBB</c>, alpha first. Theme authors copy values out of CSS,
    /// so this deliberately differs from WPF — and getting it backwards would silently turn a
    /// 10%-opacity gold into an almost-black blue.
    /// </remarks>
    [Fact]
    public void ReadsEightDigitHexInCssOrderNotWpfOrder()
    {
        Color colour = ColorParser.Parse("#D4AF3719");

        Assert.Equal(0xD4, colour.R);
        Assert.Equal(0xAF, colour.G);
        Assert.Equal(0x37, colour.B);
        Assert.Equal(0x19, colour.A);
    }

    [Fact]
    public void ReadsFourDigitHexInCssOrder()
    {
        Color colour = ColorParser.Parse("#FA08");

        Assert.Equal(0xFF, colour.R);
        Assert.Equal(0xAA, colour.G);
        Assert.Equal(0x00, colour.B);
        Assert.Equal(0x88, colour.A);
    }

    [Theory]
    [InlineData("rgb(212, 175, 55)", 255)]
    [InlineData("rgba(212, 175, 55, 1)", 255)]
    [InlineData("rgba(212,175,55,0.5)", 128)]
    [InlineData("rgb(212 175 55 / 0.5)", 128)]
    public void ParsesRgbFunctions(string input, byte expectedAlpha)
    {
        Color colour = ColorParser.Parse(input);

        Assert.Equal(212, colour.R);
        Assert.Equal(175, colour.G);
        Assert.Equal(55, colour.B);
        Assert.Equal(expectedAlpha, colour.A);
    }

    [Fact]
    public void ParsesPercentageChannels()
    {
        Color colour = ColorParser.Parse("rgb(100%, 0%, 50%)");

        Assert.Equal(255, colour.R);
        Assert.Equal(0, colour.G);
        Assert.Equal(128, colour.B);
    }

    [Fact]
    public void ParsesNamedColours()
    {
        Assert.Equal(Colors.Transparent, ColorParser.Parse("transparent"));
        Assert.Equal(Colors.Red, ColorParser.Parse("Red"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#GGGGGG")]
    [InlineData("#12345")]
    [InlineData("rgb(1, 2)")]
    [InlineData("not a colour at all")]
    public void RejectsMalformedValuesWithAMessage(string input)
    {
        bool ok = ColorParser.TryParse(input, out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void RoundTripsThroughCssNotation()
    {
        var opaque = Color.FromRgb(0xD4, 0xAF, 0x37);
        var translucent = Color.FromArgb(0x19, 0xD4, 0xAF, 0x37);

        Assert.Equal("#D4AF37", ColorParser.ToCss(opaque));
        Assert.Equal("#D4AF3719", ColorParser.ToCss(translucent));

        Assert.Equal(opaque, ColorParser.Parse(ColorParser.ToCss(opaque)));
        Assert.Equal(translucent, ColorParser.Parse(ColorParser.ToCss(translucent)));
    }

    // ------------------------------------------------------------------ contrast

    [Fact]
    public void ContrastOfBlackOnWhiteIsTheKnownMaximum()
    {
        double ratio = ColorParser.ContrastRatio(Colors.Black, Colors.White);

        Assert.Equal(21.0, ratio, 1);
    }

    [Fact]
    public void ContrastOfAColourWithItselfIsOne()
    {
        Color gold = ColorParser.Parse("#D4AF37");

        Assert.Equal(1.0, ColorParser.ContrastRatio(gold, gold), 3);
    }

    /// <summary>
    /// The brand contract records that #5C6B8A fails AA on navy-950 and #76849F was the fix.
    /// </summary>
    /// <remarks>
    /// It also records that the regression has been made once already. This test is what stops it
    /// being made a second time.
    /// </remarks>
    [Fact]
    public void TheFaintInkFixClearsAaWhereTheOldValueDidNot()
    {
        Color navy = ColorParser.Parse("#070B1A");

        double rejected = ColorParser.ContrastRatio(ColorParser.Parse("#5C6B8A"), navy);
        double adopted = ColorParser.ContrastRatio(ColorParser.Parse("#76849F"), navy);

        Assert.True(rejected < 4.5, $"#5C6B8A was expected to fail AA but scored {rejected:0.00}.");
        Assert.True(adopted >= 4.5, $"#76849F was expected to pass AA but scored {adopted:0.00}.");
    }

    [Fact]
    public void ContrastFlattensATranslucentForegroundOntoItsBackground()
    {
        Color background = Colors.White;
        Color ghost = Color.FromArgb(0x10, 0x00, 0x00, 0x00);

        double ratio = ColorParser.ContrastRatio(ghost, background);

        // Nearly invisible black on white is nearly no contrast, not the 21:1 that ignoring
        // alpha would produce.
        Assert.True(ratio < 1.5, $"Expected a low ratio for a 6% black, got {ratio:0.00}.");
    }

    [Fact]
    public void CompositeBlendsTowardsTheUnderlyingColour()
    {
        Color result = ColorParser.Composite(
            Color.FromArgb(128, 255, 255, 255), Colors.Black);

        Assert.InRange(result.R, 126, 130);
        Assert.Equal(result.R, result.G);
        Assert.Equal(result.G, result.B);
    }

    [Fact]
    public void WithAlphaKeepsTheChannelsAndReplacesOpacity()
    {
        Color faded = ColorParser.WithAlpha(ColorParser.Parse("#D4AF37"), 0.5);

        Assert.Equal(0xD4, faded.R);
        Assert.Equal(0xAF, faded.G);
        Assert.Equal(0x37, faded.B);
        Assert.InRange(faded.A, 127, 128);
    }
}
