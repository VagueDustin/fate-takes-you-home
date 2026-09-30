// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using FateTakesYouHome.Services;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>When a failed shortcut is worth a notification, and when it would only be noise.</summary>
public sealed class ShortcutFeedbackTests
{
    private static readonly DateTime Start = new(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A key pressed again and again while the connection is down fails the same way each time.
    /// One notification says so.
    /// </summary>
    [Fact]
    public void TheSameFailureInQuickSuccessionIsShownOnce()
    {
        var filter = new RepeatedNoticeFilter(TimeSpan.FromSeconds(10));

        Assert.True(filter.ShouldShow("A shortcut did not run", "Desk lamp: Not connected.", Start));
        Assert.False(filter.ShouldShow("A shortcut did not run", "Desk lamp: Not connected.", Start.AddSeconds(2)));
        Assert.False(filter.ShouldShow("A shortcut did not run", "Desk lamp: Not connected.", Start.AddSeconds(9)));
    }

    [Fact]
    public void ADifferentFailureIsShownAtOnce()
    {
        var filter = new RepeatedNoticeFilter(TimeSpan.FromSeconds(10));

        Assert.True(filter.ShouldShow("A shortcut did not run", "Desk lamp: Not connected.", Start));
        Assert.True(filter.ShouldShow("A shortcut did not run", "Speakers: Not connected.", Start.AddSeconds(1)));
    }

    /// <summary>Measured from when it was last shown, so a key held down cannot keep it quiet for good.</summary>
    [Fact]
    public void TheSameFailureIsShownAgainOnceAWhileHasPassed()
    {
        var filter = new RepeatedNoticeFilter(TimeSpan.FromSeconds(10));

        Assert.True(filter.ShouldShow("A shortcut did not run", "Desk lamp: Not connected.", Start));

        for (int second = 1; second < 10; second++)
        {
            Assert.False(filter.ShouldShow("A shortcut did not run", "Desk lamp: Not connected.", Start.AddSeconds(second)));
        }

        Assert.True(filter.ShouldShow("A shortcut did not run", "Desk lamp: Not connected.", Start.AddSeconds(10)));
    }

    /// <summary>Having shown something else in between, the first is new again.</summary>
    [Fact]
    public void AFailureAfterADifferentOneIsShownAgain()
    {
        var filter = new RepeatedNoticeFilter(TimeSpan.FromSeconds(10));

        Assert.True(filter.ShouldShow("A shortcut did not run", "Desk lamp: Not connected.", Start));
        Assert.True(filter.ShouldShow("The default action did not run", "Not connected.", Start.AddSeconds(1)));
        Assert.True(filter.ShouldShow("A shortcut did not run", "Desk lamp: Not connected.", Start.AddSeconds(2)));
    }
}
