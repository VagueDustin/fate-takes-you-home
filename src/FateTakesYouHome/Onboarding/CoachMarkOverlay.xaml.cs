// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FateTakesYouHome.Animation;

namespace FateTakesYouHome.Onboarding;

/// <summary>One stop on the guided tour.</summary>
public sealed class TourStep
{
    public required string Title { get; init; }

    public required string Body { get; init; }

    /// <summary>
    /// Resolves the element to spotlight, evaluated when the step is shown.
    /// </summary>
    /// <remarks>
    /// A function rather than a reference because the target may not exist yet — several steps
    /// point at controls on pages the tour navigates to as it goes.
    /// </remarks>
    public Func<FrameworkElement?>? Target { get; init; }

    /// <summary>Runs before the step is shown. Used to navigate to the right page.</summary>
    public Action? Prepare { get; init; }

    /// <summary>Extra padding around the spotlight, for targets that sit tight against others.</summary>
    public double Padding { get; init; } = 8;
}

/// <summary>
/// The spotlight overlay for the first-run tour.
/// </summary>
/// <remarks>
/// <para>
/// The scrim is a single path: the whole surface with the target's rounded rectangle excluded.
/// Doing it geometrically rather than as four surrounding rectangles means the cut-out follows the
/// target's corner radius, and there are no seams where the pieces meet at fractional scale
/// factors.
/// </para>
/// <para>
/// The scrim swallows clicks on purpose. A tour that lets you press the thing it is describing
/// ends up with the user two pages away from where the next step expects them to be.
/// </para>
/// </remarks>
public partial class CoachMarkOverlay : UserControl
{
    private IReadOnlyList<TourStep> _steps = [];
    private int _index;

    public CoachMarkOverlay()
    {
        InitializeComponent();

        SizeChanged += (_, _) => Render();
        Scrim.MouseDown += (_, e) => e.Handled = true;
    }

    /// <summary>Raised when the tour ends, whether by finishing or by being skipped.</summary>
    public event EventHandler<bool>? Finished;

    public bool IsRunning => Visibility == Visibility.Visible;

    /// <summary>Starts the tour from the first step.</summary>
    public void Start(IReadOnlyList<TourStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Count == 0)
        {
            return;
        }

        _steps = steps;
        _index = 0;

        Visibility = Visibility.Visible;
        Opacity = 0;

        ShowStep();

        ThemedMotion.AnimateDouble(this, OpacityProperty, 1, MotionSpeed.PageTransition);

        Focus();
        Keyboard.Focus(NextButton);
    }

    /// <summary>Ends the tour.</summary>
    public void Stop(bool completed)
    {
        if (!IsRunning)
        {
            return;
        }

        ThemedMotion.AnimateDouble(
            this,
            OpacityProperty,
            0,
            MotionSpeed.PageTransition,
            () =>
            {
                Visibility = Visibility.Collapsed;
                Finished?.Invoke(this, completed);
            });
    }

    private void ShowStep()
    {
        TourStep step = _steps[_index];

        step.Prepare?.Invoke();

        StepCounter.Text = $"Step {_index + 1} of {_steps.Count}";
        Title.Text = step.Title;
        Body.Text = step.Body;

        BackButton.Visibility = _index == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _index == _steps.Count - 1 ? "Finish" : "Next";

        // The target may only exist once the page it lives on has been laid out, so rendering is
        // deferred a beat rather than measured against a page that is still arriving.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded, new Action(Render));
    }

    /// <summary>Positions the scrim, the spotlight and the callout for the current step.</summary>
    private void Render()
    {
        if (!IsRunning || _steps.Count == 0)
        {
            return;
        }

        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var full = new RectangleGeometry(new Rect(0, 0, width, height));

        Rect? hole = MeasureTarget(_steps[_index]);

        if (hole is { } target)
        {
            var cutout = new RectangleGeometry(target, 10, 10);

            // Exclude leaves everything outside the target, which is exactly the scrim.
            Scrim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, cutout);

            SpotlightEdge.Visibility = Visibility.Visible;
            SpotlightEdge.Width = target.Width;
            SpotlightEdge.Height = target.Height;
            SpotlightEdge.RadiusX = 10;
            SpotlightEdge.RadiusY = 10;
            Canvas.SetLeft(SpotlightEdge, target.X);
            Canvas.SetTop(SpotlightEdge, target.Y);
        }
        else
        {
            // No target: dim everything and centre the callout. Used for the opening and closing
            // steps, which are about the app as a whole rather than one control.
            Scrim.Data = full;
            SpotlightEdge.Visibility = Visibility.Collapsed;
        }

        PositionCallout(hole, width, height);
    }

    /// <summary>The target's bounds in overlay coordinates, or null when it is not on screen.</summary>
    private Rect? MeasureTarget(TourStep step)
    {
        FrameworkElement? element = step.Target?.Invoke();

        if (element is null || !element.IsVisible || element.ActualWidth <= 0)
        {
            return null;
        }

        try
        {
            GeneralTransform transform = element.TransformToVisual(this);
            Rect bounds = transform.TransformBounds(
                new Rect(0, 0, element.ActualWidth, element.ActualHeight));

            bounds.Inflate(step.Padding, step.Padding);

            // A target scrolled out of view would put the spotlight off the edge of the window.
            Rect surface = new(0, 0, ActualWidth, ActualHeight);
            return surface.IntersectsWith(bounds) ? bounds : null;
        }
        catch (InvalidOperationException)
        {
            // The element is not in the same visual tree, which happens mid-navigation.
            return null;
        }
    }

    /// <summary>
    /// Places the callout next to the spotlight, on whichever side has room.
    /// </summary>
    private void PositionCallout(Rect? hole, double width, double height)
    {
        Callout.Measure(new Size(width, height));
        Size size = Callout.DesiredSize;

        const double gap = 16;

        if (hole is not { } target)
        {
            Canvas.SetLeft(Callout, (width - size.Width) / 2);
            Canvas.SetTop(Callout, (height - size.Height) / 2);
            return;
        }

        double left;
        double top;

        // Prefer right of the target, then left, then below, then above — the first that fits.
        if (target.Right + gap + size.Width <= width)
        {
            left = target.Right + gap;
            top = target.Top;
        }
        else if (target.Left - gap - size.Width >= 0)
        {
            left = target.Left - gap - size.Width;
            top = target.Top;
        }
        else if (target.Bottom + gap + size.Height <= height)
        {
            left = target.Left;
            top = target.Bottom + gap;
        }
        else
        {
            left = target.Left;
            top = Math.Max(gap, target.Top - gap - size.Height);
        }

        // Keep the whole callout on screen regardless of which branch chose the position.
        left = Math.Clamp(left, gap, Math.Max(gap, width - size.Width - gap));
        top = Math.Clamp(top, gap, Math.Max(gap, height - size.Height - gap));

        Canvas.SetLeft(Callout, left);
        Canvas.SetTop(Callout, top);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);

        Scrim.Width = ActualWidth;
        Scrim.Height = ActualHeight;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsRunning)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Stop(completed: false);
                break;

            case Key.Right or Key.Enter:
                e.Handled = true;
                Advance(1);
                break;

            case Key.Left:
                e.Handled = true;
                Advance(-1);
                break;
        }

        base.OnKeyDown(e);
    }

    private void OnNextClicked(object sender, RoutedEventArgs e) => Advance(1);

    private void OnBackClicked(object sender, RoutedEventArgs e) => Advance(-1);

    private void OnSkipClicked(object sender, RoutedEventArgs e) => Stop(completed: false);

    private void Advance(int delta)
    {
        int next = _index + delta;

        if (next < 0)
        {
            return;
        }

        if (next >= _steps.Count)
        {
            Stop(completed: true);
            return;
        }

        _index = next;
        ShowStep();
    }
}
