// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FateTakesYouHome.Controls;

/// <summary>
/// The living part of the night sky: a handful of twinkling stars and the occasional shooting
/// star, laid over the static starfield bitmap.
/// </summary>
/// <remarks>
/// <para>
/// The bitmap underneath carries the four hundred stars; animating those would mean repainting
/// the whole backdrop every frame. This layer animates about a dozen extra points of light with
/// plain opacity storyboards — cheap enough that the render thread composites them without waking
/// the UI thread — and one streak every half minute or so.
/// </para>
/// <para>
/// It obeys the same switches as everything else: the theme's starfield ornament decides whether
/// it exists (bind <c>Visibility</c> to the theme key), and the resolved motion setting decides
/// whether it moves. When motion is off the twinkles freeze at their resting glow and the
/// shooting stars simply never arrive — the sky is still there, just becalmed.
/// </para>
/// </remarks>
public sealed class StarfieldAnimation : Grid
{
    private const int TwinkleCount = 14;

    private readonly Random _random = new();
    private readonly DispatcherTimer _shootingTimer;
    private readonly List<Ellipse> _twinkles = [];

    private Path? _streak;
    private bool _running;

    public StarfieldAnimation()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        _shootingTimer = new DispatcherTimer();
        _shootingTimer.Tick += (_, _) => LaunchShootingStar();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => Scatter();
        IsVisibleChanged += OnVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildTwinkles();
        Scatter();
        SyncToTheme();

        if (App.Current is { } app)
        {
            app.Themes.Applied += OnThemeApplied;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAll();

        if (App.Current is { } app)
        {
            app.Themes.Applied -= OnThemeApplied;
        }
    }

    private void OnThemeApplied(object? sender, Services.ThemeAppliedEventArgs e) => SyncToTheme();

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // A hidden window's storyboards keep ticking clocks for nothing. Stop them outright.
        if (IsVisible)
        {
            SyncToTheme();
        }
        else
        {
            StopAll();
        }
    }

    private void SyncToTheme()
    {
        bool shouldRun = IsVisible
                         && App.Current is { } app
                         && app.Themes.Current is { } theme
                         && theme.Motion.Enabled;

        if (shouldRun && !_running)
        {
            StartAll();
        }
        else if (!shouldRun && _running)
        {
            StopAll();
        }
    }

    // ------------------------------------------------------------------ twinkles

    private void BuildTwinkles()
    {
        if (_twinkles.Count > 0)
        {
            return;
        }

        for (int i = 0; i < TwinkleCount; i++)
        {
            double size = _random.NextDouble() < 0.25 ? 3 : 2;

            var star = new Ellipse
            {
                Width = size,
                Height = size,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Opacity = 0.25,
            };

            // Every fourth twinkle is gold, matching the bitmap's minority.
            star.SetResourceReference(
                Shape.FillProperty,
                i % 4 == 0 ? "Fate.Brush.AccentHover" : "Fate.Brush.TextPrimary");

            _twinkles.Add(star);
            Children.Add(star);
        }
    }

    /// <summary>Places the twinkles randomly. Called once per size, not per frame.</summary>
    private void Scatter()
    {
        if (ActualWidth < 1 || ActualHeight < 1)
        {
            return;
        }

        foreach (Ellipse star in _twinkles)
        {
            star.Margin = new Thickness(
                _random.NextDouble() * Math.Max(1, ActualWidth - 4),
                _random.NextDouble() * Math.Max(1, ActualHeight - 4),
                0,
                0);
        }
    }

    private void StartAll()
    {
        _running = true;

        foreach (Ellipse star in _twinkles)
        {
            var pulse = new DoubleAnimation
            {
                From = 0.08,
                To = 0.28 + (_random.NextDouble() * 0.62),
                Duration = TimeSpan.FromSeconds(1.6 + (_random.NextDouble() * 3.4)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(_random.NextDouble() * 4),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };

            star.BeginAnimation(OpacityProperty, pulse);
        }

        ScheduleShootingStar();
    }

    private void StopAll()
    {
        _running = false;
        _shootingTimer.Stop();

        foreach (Ellipse star in _twinkles)
        {
            star.BeginAnimation(OpacityProperty, null);
            star.Opacity = 0.25;
        }

        if (_streak is not null)
        {
            _streak.Opacity = 0;
        }
    }

    // ------------------------------------------------------------------ shooting stars

    private void ScheduleShootingStar()
    {
        // Rare on purpose. A meteor a second is a screensaver; one every half minute is a sky.
        _shootingTimer.Interval = TimeSpan.FromSeconds(16 + (_random.NextDouble() * 26));
        _shootingTimer.Start();
    }

    private void LaunchShootingStar()
    {
        _shootingTimer.Stop();

        if (!_running || ActualWidth < 120 || ActualHeight < 120)
        {
            ScheduleShootingStar();
            return;
        }

        _streak ??= BuildStreak();

        // The streak burns in the theme's own light. Resolved per launch — they are half a
        // minute apart — so a theme change between meteors just works.
        Color head = TryFindResource(Theming.Rendering.ThemeKeys.ColorTextPrimary) is Color c
            ? c
            : Colors.White;

        _streak.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(1, 0.5),
            EndPoint = new Point(0, 0.5),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xFF, head.R, head.G, head.B), 0),
                new GradientStop(Color.FromArgb(0x00, head.R, head.G, head.B), 1),
            },
        };

        // Starts somewhere in the upper region and falls down-and-across.
        double startX = ActualWidth * (0.15 + (_random.NextDouble() * 0.7));
        double startY = ActualHeight * (0.05 + (_random.NextDouble() * 0.35));
        double travel = 180 + (_random.NextDouble() * 140);
        bool leftward = _random.NextDouble() < 0.5;

        double angle = 24 + (_random.NextDouble() * 14);
        double radians = angle * Math.PI / 180;
        double dx = Math.Cos(radians) * travel * (leftward ? -1 : 1);
        double dy = Math.Sin(radians) * travel;

        _streak.RenderTransform = new TransformGroup
        {
            Children =
            {
                new RotateTransform(leftward ? 180 - angle : angle),
                new TranslateTransform(startX, startY),
            },
        };

        var slide = new DoubleAnimation(0, dx, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        var drop = new DoubleAnimation(0, dy, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };

        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.18)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.65, KeyTime.FromPercent(0.6)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        fade.Duration = TimeSpan.FromMilliseconds(650);

        var move = new TranslateTransform();
        var group = (TransformGroup)_streak.RenderTransform;
        group.Children.Add(move);

        move.BeginAnimation(TranslateTransform.XProperty, slide);
        move.BeginAnimation(TranslateTransform.YProperty, drop);
        _streak.BeginAnimation(OpacityProperty, fade);

        ScheduleShootingStar();
    }

    private Path BuildStreak()
    {
        // A thin wedge with a gradient tail: bright head, vanishing tail.
        var geometry = new StreamGeometry();

        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            context.LineTo(new Point(-70, -1.2), true, false);
            context.LineTo(new Point(-70, 1.2), true, false);
        }

        geometry.Freeze();

        var streak = new Path
        {
            Data = geometry,
            Width = 4,
            Height = 4,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        Children.Add(streak);
        return streak;
    }
}
