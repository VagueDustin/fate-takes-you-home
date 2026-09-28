// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace FateTakesYouHome.ViewModels;

/// <summary>Where one widget sits on the layout grid, in cells.</summary>
public readonly record struct GridCell(int X, int Y, int W, int H)
{
    public bool Overlaps(GridCell other) =>
        X < other.X + other.W && other.X < X + W
        && Y < other.Y + other.H && other.Y < Y + H;
}

/// <summary>
/// Makes room on the layout grid for a widget being dragged or resized, the way a phone's home
/// screen does.
/// </summary>
/// <remarks>
/// <para>
/// The editor used to refuse any drop that overlapped another widget and send the card home. On a
/// grid that is already full, which the standard layout always is, that refused nearly every move:
/// the only place left to drop anything was the row beneath everything else.
/// </para>
/// <para>
/// Now the widget goes where it is dropped and the others make way. A widget dropped squarely on
/// one of the same size swaps places with it, which is what moving one pin onto another means.
/// Anything else in the way is pushed down, and then everything floats up to close the gaps, so
/// moving the full-width Activity strip below the first row of pins leaves no hole where it was.
/// </para>
/// <para>
/// Every arrangement is computed from where the widgets were when the drag began, never from the
/// previous step of the drag. Dragging across the grid and back therefore puts everything back
/// exactly as it was, rather than leaving a trail of shoved widgets behind.
/// </para>
/// </remarks>
public static class GridReflow
{
    /// <summary>
    /// Arranges <paramref name="start"/> with the widget at <paramref name="moved"/> placed at
    /// <paramref name="target"/>.
    /// </summary>
    /// <param name="start">Every widget's cells when the drag began. Not modified.</param>
    /// <param name="moved">Index of the widget being dragged or resized.</param>
    /// <param name="target">Where it is being put, and at what size.</param>
    /// <param name="settleMoved">
    /// False while dragging, so the moved widget stays exactly under the pointer and the others
    /// arrange around it. True on drop, so it too floats up into any gap above it.
    /// </param>
    /// <returns>The new cells, index for index with <paramref name="start"/>. Never overlapping.</returns>
    public static GridCell[] Arrange(IReadOnlyList<GridCell> start, int moved, GridCell target, bool settleMoved)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentOutOfRangeException.ThrowIfNegative(moved);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(moved, start.Count);

        if (Swapped(start, moved, target) is { } swapped)
        {
            return swapped;
        }

        GridCell[] cells = [.. start];
        cells[moved] = target;

        // Settle the others top to bottom, so a widget is pushed below whatever ends up above it,
        // and each is placed only against those already settled: pushing one down never has to
        // look back at those after it.
        var settled = new List<int> { moved };

        foreach (int index in ReadingOrder(start, except: moved))
        {
            GridCell cell = cells[index];

            while (FirstCollision(cells, settled, cell) is { } blocker)
            {
                cell = cell with { Y = blocker.Y + blocker.H };
            }

            cells[index] = cell;
            settled.Add(index);
        }

        Compact(cells, pinned: settleMoved ? -1 : moved);
        return cells;
    }

    /// <summary>
    /// A drop onto exactly one widget of the same size trades their places.
    /// </summary>
    /// <remarks>
    /// The moved widget snaps onto the other's cells even if the drop was a little off, because a
    /// swap is what the person meant and half a cell of pointer wobble should not turn it into a
    /// push.
    /// </remarks>
    private static GridCell[]? Swapped(IReadOnlyList<GridCell> start, int moved, GridCell target)
    {
        int? only = null;

        for (int i = 0; i < start.Count; i++)
        {
            if (i == moved || !start[i].Overlaps(target))
            {
                continue;
            }

            if (only is not null)
            {
                return null;
            }

            only = i;
        }

        if (only is not { } other
            || (start[other].W, start[other].H) != (target.W, target.H)
            || (start[moved].W, start[moved].H) != (target.W, target.H))
        {
            return null;
        }

        GridCell[] cells = [.. start];
        (cells[moved], cells[other]) = (start[other], start[moved]);

        // Only the two traded places, and each lands on cells of its own shape, so this can only
        // fail on a grid that overlapped before the drag began. Then it is not a swap but a push.
        return HasOverlap(cells) ? null : cells;
    }

    /// <summary>True when any two widgets share a cell.</summary>
    public static bool HasOverlap(IReadOnlyList<GridCell> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            for (int j = i + 1; j < cells.Count; j++)
            {
                if (cells[i].Overlaps(cells[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Floats every widget, except <paramref name="pinned"/>, as far up as it will go.</summary>
    private static void Compact(GridCell[] cells, int pinned)
    {
        foreach (int index in ReadingOrder(cells, except: pinned))
        {
            GridCell cell = cells[index];

            while (cell.Y > 0 && !CollidesWithOthers(cells, index, cell with { Y = cell.Y - 1 }))
            {
                cell = cell with { Y = cell.Y - 1 };
            }

            cells[index] = cell;
        }
    }

    /// <summary>Top to bottom, then left to right: the order a person reads the grid in.</summary>
    private static IEnumerable<int> ReadingOrder(IReadOnlyList<GridCell> cells, int except) =>
        Enumerable.Range(0, cells.Count)
            .Where(i => i != except)
            .OrderBy(i => cells[i].Y)
            .ThenBy(i => cells[i].X)
            .ToList();

    private static GridCell? FirstCollision(GridCell[] cells, List<int> among, GridCell cell)
    {
        foreach (int index in among)
        {
            if (cells[index].Overlaps(cell))
            {
                return cells[index];
            }
        }

        return null;
    }

    private static bool CollidesWithOthers(GridCell[] cells, int self, GridCell cell)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            if (i != self && cells[i].Overlaps(cell))
            {
                return true;
            }
        }

        return false;
    }
}
