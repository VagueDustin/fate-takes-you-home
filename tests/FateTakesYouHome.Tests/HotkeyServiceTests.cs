// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using FateTakesYouHome.Services;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>How the hotkey registrar moves from what it holds to what the settings ask for.</summary>
public sealed class HotkeyRegistrationPlanTests
{
    private static HotkeyGesture Keys(string text) => HotkeyGesture.Parse(text)!;

    private static Dictionary<string, HotkeyGesture> Map(params (string Key, string Gesture)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => Keys(p.Gesture));

    /// <summary>Typing a shortcut's name re-applies everything; none of it may blink out.</summary>
    [Fact]
    public void AnUnchangedSetTouchesNothing()
    {
        Dictionary<string, HotkeyGesture> held = Map(("OpenPanel", "Ctrl+Alt+H"), ("device:a", "Alt+1"));

        (IReadOnlyList<string> release, IReadOnlyList<string> claim) =
            HotkeyService.PlanChanges(held, Map(("OpenPanel", "Ctrl+Alt+H"), ("device:a", "alt+1")));

        Assert.Empty(release);
        Assert.Empty(claim);
    }

    [Fact]
    public void ANewCombinationReplacesOnlyItsOwnRegistration()
    {
        (IReadOnlyList<string> release, IReadOnlyList<string> claim) = HotkeyService.PlanChanges(
            Map(("OpenPanel", "Ctrl+Alt+H"), ("device:a", "Alt+1")),
            Map(("OpenPanel", "Ctrl+Alt+H"), ("device:a", "Alt+2")));

        Assert.Equal(new[] { "device:a" }, release);
        Assert.Equal(new[] { "device:a" }, claim);
    }

    [Fact]
    public void ACombinationMovingBetweenShortcutsIsReleasedBeforeItIsClaimed()
    {
        (IReadOnlyList<string> release, IReadOnlyList<string> claim) = HotkeyService.PlanChanges(
            Map(("device:a", "Alt+1")),
            Map(("device:b", "Alt+1")));

        Assert.Equal(new[] { "device:a" }, release);
        Assert.Equal(new[] { "device:b" }, claim);
    }

    /// <summary>A key another application held last time is asked for again, in case it has let go.</summary>
    [Fact]
    public void AShortcutThatFailedIsTriedAgain()
    {
        (_, IReadOnlyList<string> claim) = HotkeyService.PlanChanges(
            Map(),
            Map(("device:a", "Alt+1")));

        Assert.Equal(new[] { "device:a" }, claim);
    }
}
