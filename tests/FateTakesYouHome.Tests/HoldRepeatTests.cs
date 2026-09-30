// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.Services;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>How a held brightness or volume key keeps stepping, and when it stops.</summary>
public sealed class HoldRepeatTests
{
    private static readonly DateTime Pressed = new(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan HoldDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(250);

    private static HoldRepeat PressedAt(DateTime when)
    {
        var hold = new HoldRepeat(HoldDelay, StepInterval);
        hold.Pressed(when);
        return hold;
    }

    private static DateTime After(int milliseconds) => Pressed.AddMilliseconds(milliseconds);

    /// <summary>A tap is one step. The press took it; letting go straight after takes no more.</summary>
    [Fact]
    public void ATapIsOneStep()
    {
        HoldRepeat hold = PressedAt(Pressed);

        Assert.False(hold.ShouldStep(After(120), keysDown: false, stepInFlight: false));
        Assert.False(hold.IsActive);
    }

    /// <summary>A key held a moment too long is still a press, not the start of a ramp.</summary>
    [Fact]
    public void NothingRepeatsBeforeTheRepeatDelay()
    {
        HoldRepeat hold = PressedAt(Pressed);

        Assert.False(hold.ShouldStep(After(300), keysDown: true, stepInFlight: false));
        Assert.False(hold.ShouldStep(After(480), keysDown: true, stepInFlight: false));
        Assert.True(hold.ShouldStep(After(500), keysDown: true, stepInFlight: false));
    }

    /// <summary>Once repeating, at its own pace rather than the keyboard's thirty a second.</summary>
    [Fact]
    public void AHeldKeyStepsAtItsOwnPace()
    {
        HoldRepeat hold = PressedAt(Pressed);

        Assert.True(hold.ShouldStep(After(500), keysDown: true, stepInFlight: false));
        Assert.False(hold.ShouldStep(After(540), keysDown: true, stepInFlight: false));
        Assert.False(hold.ShouldStep(After(700), keysDown: true, stepInFlight: false));
        Assert.True(hold.ShouldStep(After(750), keysDown: true, stepInFlight: false));
        Assert.True(hold.ShouldStep(After(1000), keysDown: true, stepInFlight: false));
    }

    /// <summary>
    /// A slow connection slows the ramp rather than queueing steps that would carry on after the
    /// key is let go.
    /// </summary>
    [Fact]
    public void NoStepIsTakenOverOneStillOnItsWay()
    {
        HoldRepeat hold = PressedAt(Pressed);

        Assert.False(hold.ShouldStep(After(500), keysDown: true, stepInFlight: true));
        Assert.False(hold.ShouldStep(After(900), keysDown: true, stepInFlight: true));
        Assert.True(hold.ShouldStep(After(940), keysDown: true, stepInFlight: false));

        // Spaced from the late step, not from when it was first due.
        Assert.False(hold.ShouldStep(After(1000), keysDown: true, stepInFlight: false));
        Assert.True(hold.ShouldStep(After(1190), keysDown: true, stepInFlight: false));
    }

    /// <summary>Let go, and it is over: holding the keys again is a new press, which Windows reports.</summary>
    [Fact]
    public void LettingGoEndsTheHold()
    {
        HoldRepeat hold = PressedAt(Pressed);

        Assert.True(hold.ShouldStep(After(500), keysDown: true, stepInFlight: false));
        Assert.False(hold.ShouldStep(After(600), keysDown: false, stepInFlight: false));
        Assert.False(hold.ShouldStep(After(2000), keysDown: true, stepInFlight: false));
        Assert.False(hold.IsActive);
    }

    [Fact]
    public void AFailedStepEndsTheHold()
    {
        HoldRepeat hold = PressedAt(Pressed);

        hold.Stop();

        Assert.False(hold.ShouldStep(After(500), keysDown: true, stepInFlight: false));
    }

    /// <summary>A quick second press waits out the delay again rather than ramping at once.</summary>
    [Fact]
    public void ASecondPressStartsTheHoldOver()
    {
        HoldRepeat hold = PressedAt(Pressed);

        hold.Pressed(After(400));

        Assert.False(hold.ShouldStep(After(700), keysDown: true, stepInFlight: false));
        Assert.True(hold.ShouldStep(After(900), keysDown: true, stepInFlight: false));
    }

    [Theory]
    [InlineData(DeviceAction.BrightnessUp, true)]
    [InlineData(DeviceAction.BrightnessDown, true)]
    [InlineData(DeviceAction.VolumeUp, true)]
    [InlineData(DeviceAction.VolumeDown, true)]
    [InlineData(DeviceAction.SetBrightness, false)]
    [InlineData(DeviceAction.Toggle, false)]
    [InlineData(DeviceAction.NextTrack, false)]
    [InlineData(DeviceAction.ToggleMute, false)]
    public void OnlyStepsRepeatWhileHeld(DeviceAction action, bool repeats)
    {
        Assert.Equal(repeats, DeviceActions.IsStep(action));
    }
}
