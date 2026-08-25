// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FateTakesYouHome.Theming.Model;
using FateTakesYouHome.Theming.Rendering;

namespace FateTakesYouHome.Animation;

/// <summary>Which of the theme's durations an animation should use.</summary>
public enum MotionSpeed
{
    Hover,
    Press,
    FlyoutOpen,
    FlyoutClose,
    PageTransition,
}

/// <summary>
/// Runs animations using the live theme's durations, easing and concurrency budget.
/// </summary>
/// <remarks>
/// <para>
/// Animations live here rather than in XAML because a WPF <see cref="Storyboard"/> is a
/// <see cref="Freezable"/>: once a template containing one is sealed, the storyboard is frozen and
/// its <c>Duration</c> can no longer be a <c>DynamicResource</c>. A theme that cannot change how
/// fast things move is not much of a theme, so the animations are built at the point of use where
/// the current values can be read.
/// </para>
/// <para>
/// The ornament tier's <c>maxConcurrentAnimations</c> is enforced here too. Past the budget,
/// animations are applied instantly rather than queued — a late animation looks worse than no
/// animation, and the whole point of the cap is that the interface stays calm.
/// </para>
/// </remarks>
public static class ThemedMotion
{
    private static int _active;

    /// <summary>The theme currently published in the application resources.</summary>
    public static Theme? Current =>
        Application.Current?.TryFindResource(ThemeKeys.Theme) as Theme;

    /// <summary>False when the theme, the user, or Windows has turned motion off.</summary>
    public static bool IsEnabled => Current?.Motion.Enabled ?? false;

    /// <summary>How many animations are running right now.</summary>
    public static int ActiveCount => _active;

    public static TimeSpan DurationOf(MotionSpeed speed)
    {
        ThemeMotion? motion = Current?.Motion;

        if (motion is null || !motion.Enabled)
        {
            return TimeSpan.Zero;
        }

        return speed switch
        {
            MotionSpeed.Hover => motion.Hover,
            MotionSpeed.Press => motion.Press,
            MotionSpeed.FlyoutOpen => motion.FlyoutOpen,
            MotionSpeed.FlyoutClose => motion.FlyoutClose,
            MotionSpeed.PageTransition => motion.PageTransition,
            _ => motion.Hover,
        };
    }

    public static IEasingFunction EasingOf(MotionSpeed speed)
    {
        ThemeMotion motion = Current?.Motion ?? new ThemeMotion();

        EasingSpec spec = speed is MotionSpeed.FlyoutOpen or MotionSpeed.FlyoutClose
            ? motion.FlyoutEasing
            : motion.StandardEasing;

        return CubicBezierEase.Create(spec);
    }

    /// <summary>
    /// Animates a double property, or sets it directly when motion is unavailable.
    /// </summary>
    /// <param name="onCompleted">Runs when the animation finishes, or immediately if it was skipped.</param>
    public static void AnimateDouble(
        IAnimatable target,
        DependencyProperty property,
        double to,
        MotionSpeed speed,
        Action? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        TimeSpan duration = DurationOf(speed);

        if (duration <= TimeSpan.Zero || !TryReserveSlot())
        {
            // Clearing the animation first, otherwise the held animated value wins over the set.
            target.BeginAnimation(property, null);

            if (target is DependencyObject obj)
            {
                obj.SetValue(property, to);
            }

            onCompleted?.Invoke();
            return;
        }

        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(duration),
            EasingFunction = EasingOf(speed),

            // Holding the final value keeps the property under animation control, which avoids a
            // one-frame snap back to the local value when the animation completes.
            FillBehavior = FillBehavior.HoldEnd,
        };

        animation.Completed += (_, _) =>
        {
            ReleaseSlot();
            onCompleted?.Invoke();
        };

        target.BeginAnimation(property, animation);
    }

    /// <summary>Animates a colour-valued brush property by animating the brush's own colour.</summary>
    public static void AnimateBrushColour(
        SolidColorBrush brush, Color to, MotionSpeed speed)
    {
        ArgumentNullException.ThrowIfNull(brush);

        if (brush.IsFrozen)
        {
            throw new ArgumentException(
                "A frozen brush cannot be animated. Give the control its own brush instance.",
                nameof(brush));
        }

        TimeSpan duration = DurationOf(speed);

        if (duration <= TimeSpan.Zero || !TryReserveSlot())
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = to;
            return;
        }

        var animation = new ColorAnimation
        {
            To = to,
            Duration = new Duration(duration),
            EasingFunction = EasingOf(speed),
            FillBehavior = FillBehavior.HoldEnd,
        };

        animation.Completed += (_, _) => ReleaseSlot();

        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    /// <summary>Cancels an animation and leaves the property at a fixed value.</summary>
    public static void Snap(IAnimatable target, DependencyProperty property, double value)
    {
        target.BeginAnimation(property, null);

        if (target is DependencyObject obj)
        {
            obj.SetValue(property, value);
        }
    }

    /// <summary>
    /// The delay before item <paramref name="index"/> should appear in a staggered entrance.
    /// </summary>
    /// <remarks>
    /// Items past <c>staggerMaxItems</c> all share the last delay. A list of sixty entities
    /// staggered at 24 ms each would take a second and a half to finish appearing, which stops
    /// reading as polish and starts reading as slow.
    /// </remarks>
    public static TimeSpan StaggerDelay(int index)
    {
        Theme? theme = Current;

        if (theme is null || !theme.Motion.Enabled || !theme.Ornament.StaggerEntrances)
        {
            return TimeSpan.Zero;
        }

        int capped = Math.Min(index, Math.Max(0, theme.Motion.StaggerMaxItems));
        return TimeSpan.FromMilliseconds(theme.Motion.StaggerStep.TotalMilliseconds * capped);
    }

    /// <summary>
    /// Claims one of the tier's concurrent animation slots.
    /// </summary>
    /// <remarks>
    /// Deliberately not thread-safe beyond an interlocked counter: every caller is on the UI
    /// thread, and the cost of being occasionally off by one is that one extra animation runs.
    /// </remarks>
    private static bool TryReserveSlot()
    {
        int cap = Current?.Ornament.MaxConcurrentAnimations ?? 3;

        if (Interlocked.Increment(ref _active) <= cap)
        {
            return true;
        }

        Interlocked.Decrement(ref _active);
        return false;
    }

    private static void ReleaseSlot()
    {
        if (Interlocked.Decrement(ref _active) < 0)
        {
            // Should not happen, but a negative count would permanently disable animation.
            Interlocked.Exchange(ref _active, 0);
        }
    }

    /// <summary>Resets the budget. Called when a theme is applied, in case anything leaked.</summary>
    public static void ResetBudget() => Interlocked.Exchange(ref _active, 0);
}
