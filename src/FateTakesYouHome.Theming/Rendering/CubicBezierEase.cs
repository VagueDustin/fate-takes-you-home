// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Media.Animation;
using FateTakesYouHome.Theming.Model;

namespace FateTakesYouHome.Theming.Rendering;

/// <summary>
/// A CSS-compatible <c>cubic-bezier(x1, y1, x2, y2)</c> easing function.
/// </summary>
/// <remarks>
/// <para>
/// WPF's built-in easings cannot express the brand's <c>--ease</c> and <c>--spring</c> curves:
/// <see cref="CubicEase"/> has fixed control points, and <see cref="BackEase"/> overshoots on a
/// different shape entirely. Since every theme's motion is specified as a cubic Bézier, the app
/// needs one that actually solves the curve.
/// </para>
/// <para>
/// The curve is parametric — x and y are both functions of an internal parameter t, and t is not
/// the animation's progress. Evaluating it therefore means solving x(t) = progress first, which is
/// done with Newton–Raphson and a bisection fallback for the flat regions where the derivative
/// approaches zero.
/// </para>
/// </remarks>
public sealed class CubicBezierEase : EasingFunctionBase
{
    private const int NewtonIterations = 8;
    private const double NewtonMinSlope = 1e-3;
    private const double SubdivisionPrecision = 1e-7;
    private const int SubdivisionMaxIterations = 12;

    public static readonly DependencyProperty X1Property = DependencyProperty.Register(
        nameof(X1), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.0));

    public static readonly DependencyProperty Y1Property = DependencyProperty.Register(
        nameof(Y1), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.0));

    public static readonly DependencyProperty X2Property = DependencyProperty.Register(
        nameof(X2), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(1.0));

    public static readonly DependencyProperty Y2Property = DependencyProperty.Register(
        nameof(Y2), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(1.0));

    public CubicBezierEase()
    {
    }

    public CubicBezierEase(EasingSpec spec)
    {
        X1 = spec.X1;
        Y1 = spec.Y1;
        X2 = spec.X2;
        Y2 = spec.Y2;

        // The spec's curve is the whole easing; applying WPF's own In/Out wrapper on top would
        // mirror it and produce something nobody asked for.
        EasingMode = EasingMode.EaseIn;
    }

    /// <summary>First control point's x, clamped to 0–1 as CSS requires.</summary>
    public double X1
    {
        get => (double)GetValue(X1Property);
        set => SetValue(X1Property, value);
    }

    public double Y1
    {
        get => (double)GetValue(Y1Property);
        set => SetValue(Y1Property, value);
    }

    /// <summary>Second control point's x, clamped to 0–1 as CSS requires.</summary>
    public double X2
    {
        get => (double)GetValue(X2Property);
        set => SetValue(X2Property, value);
    }

    public double Y2
    {
        get => (double)GetValue(Y2Property);
        set => SetValue(Y2Property, value);
    }

    protected override double EaseInCore(double normalizedTime)
    {
        double x1 = Math.Clamp(X1, 0, 1);
        double x2 = Math.Clamp(X2, 0, 1);

        // A straight line needs no solving, and this is the common "linear" case.
        if (x1 == Y1 && x2 == Y2)
        {
            return normalizedTime;
        }

        if (normalizedTime <= 0)
        {
            return 0;
        }

        if (normalizedTime >= 1)
        {
            return 1;
        }

        double t = SolveForX(normalizedTime, x1, x2);
        return Bezier(t, Y1, Y2);
    }

    protected override Freezable CreateInstanceCore() => new CubicBezierEase();

    /// <summary>Evaluates one axis of the curve at parameter <paramref name="t"/>.</summary>
    /// <remarks>The end points are fixed at 0 and 1, so only the two control values are needed.</remarks>
    private static double Bezier(double t, double c1, double c2)
    {
        double inverse = 1 - t;

        return (3 * inverse * inverse * t * c1)
             + (3 * inverse * t * t * c2)
             + (t * t * t);
    }

    /// <summary>The derivative of <see cref="Bezier"/> with respect to t.</summary>
    private static double Slope(double t, double c1, double c2)
    {
        double inverse = 1 - t;

        return (3 * inverse * inverse * c1)
             + (6 * inverse * t * (c2 - c1))
             + (3 * t * t * (1 - c2));
    }

    /// <summary>Finds the parameter t at which x(t) equals <paramref name="x"/>.</summary>
    private static double SolveForX(double x, double x1, double x2)
    {
        // Newton–Raphson converges in two or three steps across most of the curve.
        double t = x;

        for (int i = 0; i < NewtonIterations; i++)
        {
            double slope = Slope(t, x1, x2);

            if (Math.Abs(slope) < NewtonMinSlope)
            {
                // Nearly flat: Newton would shoot off. Fall through to bisection.
                break;
            }

            double error = Bezier(t, x1, x2) - x;

            if (Math.Abs(error) < SubdivisionPrecision)
            {
                return t;
            }

            t -= error / slope;
        }

        // Bisection is slower but cannot diverge, which matters for spring curves whose control
        // points put a near-horizontal segment in the middle of the range.
        double low = 0;
        double high = 1;
        t = Math.Clamp(t, 0, 1);

        for (int i = 0; i < SubdivisionMaxIterations; i++)
        {
            double current = Bezier(t, x1, x2);
            double error = current - x;

            if (Math.Abs(error) < SubdivisionPrecision)
            {
                break;
            }

            if (error > 0)
            {
                high = t;
            }
            else
            {
                low = t;
            }

            t = (low + high) / 2;
        }

        return t;
    }

    /// <summary>Builds a frozen easing function, safe to share across every animation in the app.</summary>
    public static IEasingFunction Create(EasingSpec spec)
    {
        var ease = new CubicBezierEase(spec);
        ease.Freeze();
        return ease;
    }
}
