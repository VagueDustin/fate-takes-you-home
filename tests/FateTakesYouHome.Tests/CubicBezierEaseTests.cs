// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows.Media.Animation;
using FateTakesYouHome.Theming.Model;
using FateTakesYouHome.Theming.Rendering;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// The easing solver.
/// </summary>
/// <remarks>
/// This is the one piece of real numerical code in the project — a Newton–Raphson solve with a
/// bisection fallback — and it runs on every frame of every animation. A curve that overshoots its
/// endpoints or fails to converge shows up as a visible jolt, so the properties are pinned here
/// rather than judged by eye.
/// </remarks>
public sealed class CubicBezierEaseTests
{
    private static double At(EasingSpec spec, double progress) =>
        CubicBezierEase.Create(spec).Ease(progress);

    [Theory]
    [InlineData(0, 0, 1, 1)]      // Linear
    [InlineData(0.2, 0.7, 0.3, 1)] // The brand's standard curve
    [InlineData(0.3, 1.5, 0.4, 1)] // The brand's overshoot curve
    [InlineData(0.42, 0, 0.58, 1)] // ease-in-out
    public void TheCurveStartsAtZeroAndEndsAtOne(double x1, double y1, double x2, double y2)
    {
        var spec = new EasingSpec(x1, y1, x2, y2);

        Assert.Equal(0, At(spec, 0), 6);
        Assert.Equal(1, At(spec, 1), 6);
    }

    [Fact]
    public void ALinearCurveIsTheIdentity()
    {
        for (double t = 0; t <= 1.0001; t += 0.05)
        {
            Assert.Equal(t, At(EasingSpec.Linear, Math.Min(t, 1)), 4);
        }
    }

    /// <summary>
    /// The standard curve must be monotonic: an animation that goes backwards mid-flight reads as
    /// a stutter.
    /// </summary>
    [Fact]
    public void TheStandardCurveNeverGoesBackwards()
    {
        double previous = -1;

        for (int step = 0; step <= 200; step++)
        {
            double value = At(EasingSpec.FateOut, step / 200d);

            Assert.True(
                value >= previous - 1e-9,
                $"The curve went backwards at t={step / 200d:0.000}: {previous:0.0000} then {value:0.0000}.");

            previous = value;
        }
    }

    /// <summary>The standard curve is an ease-out: most of the distance is covered early.</summary>
    [Fact]
    public void TheStandardCurveFrontLoadsTheMovement()
    {
        double halfway = At(EasingSpec.FateOut, 0.5);

        Assert.True(halfway > 0.5, $"Expected an ease-out, but t=0.5 gave {halfway:0.000}.");
    }

    /// <summary>
    /// The spring curve is supposed to overshoot. That is the whole point of it, and a solver
    /// that clamped the output would quietly turn it into an ordinary ease.
    /// </summary>
    [Fact]
    public void TheSpringCurveOvershootsPastOne()
    {
        double peak = 0;

        for (int step = 0; step <= 200; step++)
        {
            peak = Math.Max(peak, At(EasingSpec.FateSpring, step / 200d));
        }

        Assert.True(peak > 1.0, $"The spring curve peaked at {peak:0.000}; it should exceed 1.");
    }

    /// <summary>
    /// A curve with a near-flat segment is where Newton–Raphson diverges and the bisection
    /// fallback has to take over.
    /// </summary>
    [Fact]
    public void ANearlyFlatCurveStillSolves()
    {
        var pathological = new EasingSpec(1, 0, 0, 1);

        for (int step = 0; step <= 100; step++)
        {
            double value = At(pathological, step / 100d);

            Assert.False(double.IsNaN(value), $"Solver produced NaN at t={step / 100d:0.00}.");
            Assert.InRange(value, -0.001, 1.001);
        }
    }

    [Fact]
    public void ProgressOutsideTheRangeIsClamped()
    {
        Assert.Equal(0, At(EasingSpec.FateOut, -1), 6);
        Assert.Equal(1, At(EasingSpec.FateOut, 2), 6);
    }

    [Fact]
    public void TheCreatedFunctionIsFrozenSoItCanBeSharedAcrossAnimations()
    {
        IEasingFunction ease = CubicBezierEase.Create(EasingSpec.FateOut);

        Assert.True(((System.Windows.Freezable)ease).IsFrozen);
    }

    [Fact]
    public void TheSpecFormatsAsCssForRoundTripping()
    {
        Assert.Equal("cubic-bezier(0.2, 0.7, 0.3, 1)", EasingSpec.FateOut.ToString());
    }
}
