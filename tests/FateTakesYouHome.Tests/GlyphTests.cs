// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Media;
using FateTakesYouHome.Animation;
using FateTakesYouHome.Controls;
using FateTakesYouHome.HomeAssistant.Models;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// The entity glyphs' shared frame, and the measurement the draw-in animation depends on.
/// </summary>
public sealed class GlyphTests
{
    [Theory]
    [InlineData(HaDomains.Light, null)]
    [InlineData(HaDomains.Switch, "outlet")]
    [InlineData(HaDomains.Number, null)]
    [InlineData(HaDomains.BinarySensor, "smoke")]
    [InlineData("not_a_domain", null)]
    public void EveryGlyphIsFramedOnTheWholeGrid(string domain, string? deviceClass)
    {
        // Stretch="Uniform" scales to these bounds; a glyph that sized itself to its own ink would
        // come out larger or smaller than its neighbours.
        Rect bounds = EntityGlyphs.For(domain, deviceClass).Bounds;

        Assert.Equal(new Rect(0, 0, EntityGlyphs.DesignSize, EntityGlyphs.DesignSize), bounds);
    }

    [Fact]
    public void TheLongestFigureIsMeasuredIncludingItsClosingEdge()
    {
        // A closed 10 by 10 square, a 5-unit line, and the frame's empty figures.
        Geometry geometry = Geometry.Parse("M0,0 M24,24 M2,2 L12,2 L12,12 L2,12 Z M2,20 L7,20");

        Assert.Equal(40, GlyphMotion.LongestFigure(geometry), precision: 6);
    }

    [Fact]
    public void CurvesAreMeasuredAlongTheCurve()
    {
        // A full circle of radius 5, drawn as two arcs.
        Geometry geometry = Geometry.Parse("M0,5 A5,5 0 1 1 10,5 A5,5 0 1 1 0,5 Z");

        Assert.Equal(2 * Math.PI * 5, GlyphMotion.LongestFigure(geometry), precision: 1);
    }
}
