// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using FateTakesYouHome.Controls;
using FateTakesYouHome.Models;
using FateTakesYouHome.ViewModels;
using FateTakesYouHome.Views.Pages;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>The arithmetic behind dragging a card on the layout editor.</summary>
public sealed class LayoutDragTests
{
    private const double Column = 100;
    private const double Row = 94;

    // ------------------------------------------------------------------ snapping

    /// <summary>
    /// A card drawn most of the way into the next column belongs to that column; truncating would
    /// leave the drop outline a whole cell behind the card.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(49, 46, 0, 0)]
    [InlineData(50, 47, 1, 1)]
    [InlineData(190, 180, 2, 2)]
    [InlineData(-30, -40, 0, 0)]
    public void ACardSettlesIntoTheNearestCell(double left, double top, int column, int row)
    {
        (int x, int y) = WidgetCanvas.NearestCell(new Point(left, top), Column, Row);

        // Negative results are clamped by the caller, which knows the grid's width.
        Assert.Equal(column, Math.Max(0, x));
        Assert.Equal(row, Math.Max(0, y));
    }

    // ------------------------------------------------------------------ scrolling

    [Fact]
    public void NothingScrollsWhileThePointerIsInTheMiddle()
    {
        Assert.Equal(0, LayoutPage.AutoScrollStep(300, 600));
    }

    [Fact]
    public void ScrollingSpeedsUpTowardsTheEdge()
    {
        double near = LayoutPage.AutoScrollStep(600 - (LayoutPage.ScrollEdge * 0.25), 600);
        double nearer = LayoutPage.AutoScrollStep(600 - (LayoutPage.ScrollEdge * 0.1), 600);

        Assert.True(near > 0);
        Assert.True(nearer > near);
    }

    [Fact]
    public void PastTheEdgeScrollsAtFullSpeedInTheRightDirection()
    {
        Assert.Equal(LayoutPage.MaxScrollStep, LayoutPage.AutoScrollStep(900, 600));
        Assert.Equal(-LayoutPage.MaxScrollStep, LayoutPage.AutoScrollStep(-200, 600));
    }

    /// <summary>In a short editor the two edge bands would otherwise meet, and it would never hold still.</summary>
    [Fact]
    public void AShortEditorStillHasSomewhereToHoldStill()
    {
        Assert.Equal(0, LayoutPage.AutoScrollStep(50, 100));
    }

    [Fact]
    public void AnEditorWithNoHeightNeverScrolls()
    {
        Assert.Equal(0, LayoutPage.AutoScrollStep(10, 0));
    }

    // ------------------------------------------------------------------ the floor

    private static EditorWidget Card(int x, int y, int w, int h) =>
        new(new WidgetSpec { X = x, Y = y, W = w, H = h }, "", "");

    /// <summary>Without a floor, scrolling while dragging downwards would grow the canvas forever.</summary>
    [Fact]
    public void ACardCanGoNoFurtherThanDirectlyBeneathTheOthers()
    {
        EditorWidget dragged = Card(0, 0, 2, 1);
        EditorWidget[] all = [dragged, Card(0, 1, 2, 2), Card(2, 0, 2, 4)];

        Assert.Equal(4, LayoutEditorViewModel.DeepestRow(dragged, all));
    }

    [Fact]
    public void TheDraggedCardDoesNotHoldItsOwnFloorDown()
    {
        EditorWidget dragged = Card(0, 5, 2, 3);

        Assert.Equal(1, LayoutEditorViewModel.DeepestRow(dragged, [dragged, Card(2, 0, 2, 1)]));
        Assert.Equal(0, LayoutEditorViewModel.DeepestRow(dragged, [dragged]));
    }
}
