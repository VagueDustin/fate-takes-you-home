// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using FateTakesYouHome.ViewModels;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>How the layout grid makes room for a dragged widget.</summary>
public sealed class GridReflowTests
{
    /// <summary>
    /// The standard home layout, six columns: Activity across the top, four pins two to a row, and
    /// Rooms across the bottom. Every cell is taken, which is what made the old refuse-and-return
    /// behaviour refuse nearly every drop.
    /// </summary>
    private static GridCell[] StandardHome() =>
    [
        new(0, 0, 6, 1), // 0 Activity
        new(0, 1, 3, 1), // 1 pin
        new(3, 1, 3, 1), // 2 pin
        new(0, 2, 3, 1), // 3 pin
        new(3, 2, 3, 1), // 4 pin
        new(0, 3, 6, 2), // 5 Rooms
    ];

    [Fact]
    public void APinDroppedOnAnotherPinSwapsWithIt()
    {
        GridCell[] start = StandardHome();

        GridCell[] after = GridReflow.Arrange(start, 1, new GridCell(3, 2, 3, 1), settleMoved: true);

        Assert.Equal(start[4], after[1]);
        Assert.Equal(start[1], after[4]);
        Assert.Equal(new[] { start[0], start[2], start[3], start[5] }, new[] { after[0], after[2], after[3], after[5] });
    }

    /// <summary>A little wobble off the other card's cells is still the swap that was meant.</summary>
    [Fact]
    public void ASwapSnapsOntoTheOtherCard()
    {
        GridCell[] start = StandardHome();

        GridCell[] after = GridReflow.Arrange(start, 1, new GridCell(2, 1, 3, 1), settleMoved: false);

        Assert.Equal(start[2], after[1]);
        Assert.Equal(start[1], after[2]);
    }

    /// <summary>The report that started this: the top card could not be moved anywhere.</summary>
    [Fact]
    public void TheTopStripCanMoveBelowTheFirstRowOfPins()
    {
        GridCell[] after = GridReflow.Arrange(StandardHome(), 0, new GridCell(0, 2, 6, 1), settleMoved: true);

        Assert.Equal(new GridCell(0, 0, 3, 1), after[1]);
        Assert.Equal(new GridCell(3, 0, 3, 1), after[2]);
        Assert.Equal(new GridCell(0, 1, 6, 1), after[0]);
        Assert.Equal(new GridCell(0, 2, 3, 1), after[3]);
        Assert.Equal(new GridCell(3, 2, 3, 1), after[4]);
        Assert.Equal(new GridCell(0, 3, 6, 2), after[5]);
        Assert.False(GridReflow.HasOverlap(after));
    }

    [Fact]
    public void WhatIsInTheWayIsPushedDownAndTheGapCloses()
    {
        // Rooms dragged to the top: everything else moves down beneath it, and nothing is left
        // hanging where Rooms used to be.
        GridCell[] after = GridReflow.Arrange(StandardHome(), 5, new GridCell(0, 0, 6, 2), settleMoved: true);

        Assert.Equal(new GridCell(0, 0, 6, 2), after[5]);
        Assert.Equal(2, after[0].Y);
        Assert.Equal(3, after[1].Y);
        Assert.Equal(4, after[3].Y);
        Assert.Equal(5, after.Max(c => c.Y + c.H));
    }

    /// <summary>Arrangements are computed from where things began, so dragging back undoes everything.</summary>
    [Fact]
    public void DraggingBackToWhereItStartedChangesNothing()
    {
        GridCell[] start = StandardHome();

        GridReflow.Arrange(start, 0, new GridCell(0, 3, 6, 1), settleMoved: false);
        GridCell[] back = GridReflow.Arrange(start, 0, start[0], settleMoved: false);

        Assert.Equal(start, back);
    }

    [Fact]
    public void WhileDraggingTheCardStaysUnderThePointer()
    {
        GridCell target = new(0, 9, 3, 1);

        GridCell[] during = GridReflow.Arrange(StandardHome(), 1, target, settleMoved: false);
        GridCell[] dropped = GridReflow.Arrange(StandardHome(), 1, target, settleMoved: true);

        Assert.Equal(target, during[1]);
        Assert.True(dropped[1].Y < target.Y, "On drop it should float up into the space below the others.");
    }

    [Fact]
    public void GrowingAWidgetPushesItsNeighboursDown()
    {
        GridCell[] after = GridReflow.Arrange(StandardHome(), 1, new GridCell(0, 1, 3, 2), settleMoved: true);

        Assert.Equal(new GridCell(0, 1, 3, 2), after[1]);
        Assert.Equal(3, after[3].Y);
        Assert.False(GridReflow.HasOverlap(after));
    }

    [Fact]
    public void TheStartingArrangementIsNotModified()
    {
        GridCell[] start = StandardHome();
        GridCell[] copy = [.. start];

        GridReflow.Arrange(start, 0, new GridCell(0, 3, 6, 1), settleMoved: true);

        Assert.Equal(copy, start);
    }

    /// <summary>
    /// Whatever the layout and wherever the drop, the result never has two widgets on one cell.
    /// Randomised with a fixed seed, so a failure reproduces.
    /// </summary>
    [Fact]
    public void NoArrangementEverOverlaps()
    {
        var random = new Random(20260928);

        for (int round = 0; round < 500; round++)
        {
            GridCell[] start = RandomPackedLayout(random, columns: 6);
            int moved = random.Next(start.Length);
            int w = random.Next(1, 7);
            int h = random.Next(1, 4);
            var target = new GridCell(random.Next(0, 7 - w), random.Next(0, 10), w, h);

            GridCell[] during = GridReflow.Arrange(start, moved, target, settleMoved: false);
            GridCell[] dropped = GridReflow.Arrange(start, moved, target, settleMoved: true);

            Assert.False(GridReflow.HasOverlap(during), $"Round {round}: overlap while dragging.");
            Assert.False(GridReflow.HasOverlap(dropped), $"Round {round}: overlap after the drop.");
            Assert.All(dropped, c => Assert.True(c.X >= 0 && c.X + c.W <= 6 && c.Y >= 0, $"Round {round}: {c} left the grid."));
        }
    }

    /// <summary>A layout with no overlaps, built by dropping random widgets into the first free slot.</summary>
    private static GridCell[] RandomPackedLayout(Random random, int columns)
    {
        var cells = new List<GridCell>();
        int count = random.Next(1, 10);

        while (cells.Count < count)
        {
            int w = random.Next(1, columns + 1);
            int h = random.Next(1, 3);

            for (int y = 0; ; y++)
            {
                int x = random.Next(0, columns - w + 1);
                var candidate = new GridCell(x, y, w, h);

                if (!cells.Any(candidate.Overlaps))
                {
                    cells.Add(candidate);
                    break;
                }
            }
        }

        return [.. cells];
    }
}
