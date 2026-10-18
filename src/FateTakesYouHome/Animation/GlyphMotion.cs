// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FateTakesYouHome.Animation;

/// <summary>
/// Small motions for stroked glyphs: they draw themselves in when they become active, and lift a
/// little under the pointer.
/// </summary>
/// <remarks>
/// <para>
/// The draw-in is the stroke-dashoffset trick animated SVG icons use, done with WPF's own dash
/// properties: one dash as long as the longest figure, slid in from off the end of the path. Dash
/// patterns restart at every figure, so all of a glyph's strokes draw at once, each from its own
/// start, and the short ones finish first. Once the stroke is complete the dash is cleared, so a
/// glyph at rest renders exactly as it always did.
/// </para>
/// <para>
/// Attached properties bound to state rather than triggers in the templates, for the reason
/// <see cref="ThemedMotion"/> gives: a storyboard in a sealed template is frozen and cannot take
/// the theme's durations. Everything runs through <see cref="ThemedMotion"/>, so reduced motion,
/// a theme with motion off, and the tier's animation budget all apply without extra checks.
/// </para>
/// </remarks>
public static class GlyphMotion
{
    /// <summary>How far a glyph grows under the pointer. Enough to notice, not enough to jostle the label.</summary>
    private const double HoverScale = 1.12;

    /// <summary>When this turns true on a loaded shape, the shape draws itself in.</summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(GlyphMotion), new PropertyMetadata(false, OnIsActiveChanged));

    /// <summary>While true, the element is scaled up slightly about its centre.</summary>
    public static readonly DependencyProperty IsHoveredProperty = DependencyProperty.RegisterAttached(
        "IsHovered", typeof(bool), typeof(GlyphMotion), new PropertyMetadata(false, OnIsHoveredChanged));

    /// <summary>The data context the shape had the last time <see cref="IsActiveProperty"/> changed.</summary>
    private static readonly DependencyProperty LastContextProperty = DependencyProperty.RegisterAttached(
        "LastContext", typeof(object), typeof(GlyphMotion), new PropertyMetadata(null));

    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);

    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);

    public static bool GetIsHovered(DependencyObject element) => (bool)element.GetValue(IsHoveredProperty);

    public static void SetIsHovered(DependencyObject element, bool value) => element.SetValue(IsHoveredProperty, value);

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Shape shape)
        {
            return;
        }

        // A recycled tile container gets a new entity with the state already set; that is not the
        // entity changing state, so it must not animate. Null means nothing has been recorded yet.
        // shortcut: a container first created "off" and recycled to an "on" entity still draws in
        // once, because an unchanged default raises no change to record; harmless, so left.
        object? previous = shape.GetValue(LastContextProperty);
        shape.SetValue(LastContextProperty, shape.DataContext);
        bool sameSubject = previous is null || ReferenceEquals(previous, shape.DataContext);

        // Not before Loaded: the first binding pass is a glyph appearing, not a glyph activating,
        // and a page of thirty lit tiles all drawing in at once is noise.
        if ((bool)e.NewValue && shape.IsLoaded && sameSubject)
        {
            DrawIn(shape);
        }
    }

    private static void OnIsHoveredChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        ScaleTransform scale = EnsureScale(element);

        // With motion off the glyph stays put: a size that jumps on hover is motion too, only worse.
        double to = (bool)e.NewValue && ThemedMotion.IsEnabled ? HoverScale : 1;

        ThemedMotion.AnimateDouble(scale, ScaleTransform.ScaleXProperty, to, MotionSpeed.Hover);
        ThemedMotion.AnimateDouble(scale, ScaleTransform.ScaleYProperty, to, MotionSpeed.Hover);
    }

    private static void DrawIn(Shape shape)
    {
        if (!ThemedMotion.IsEnabled || shape.StrokeThickness <= 0)
        {
            return;
        }

        // RenderedGeometry is already stretched to the shape's size, so its lengths are the
        // device-independent pixels the dash pattern is measured in (as multiples of the stroke).
        double longest = LongestFigure(shape.RenderedGeometry);
        if (longest <= 0)
        {
            return;
        }

        // Two thicknesses of slack each way: the round cap reaches half a thickness past a dash,
        // and a cap peeking in at the start of every figure would read as a row of dots.
        double dash = (longest / shape.StrokeThickness) + 2;

        shape.StrokeDashCap = PenLineCap.Round;
        shape.StrokeDashArray = [dash, dash];
        ThemedMotion.Snap(shape, Shape.StrokeDashOffsetProperty, dash + 1);

        ThemedMotion.AnimateDouble(
            shape,
            Shape.StrokeDashOffsetProperty,
            0,
            MotionSpeed.PageTransition,
            onCompleted: () =>
            {
                shape.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                shape.ClearValue(Shape.StrokeDashOffsetProperty);
                shape.ClearValue(Shape.StrokeDashArrayProperty);
                shape.ClearValue(Shape.StrokeDashCapProperty);
            });
    }

    /// <summary>The length of the longest figure in a geometry, after flattening its curves.</summary>
    public static double LongestFigure(Geometry geometry)
    {
        PathGeometry flat = geometry.GetFlattenedPathGeometry(0.02, ToleranceType.Absolute);
        double longest = 0;

        foreach (PathFigure figure in flat.Figures)
        {
            Point last = figure.StartPoint;
            double length = 0;

            foreach (PathSegment segment in figure.Segments)
            {
                IEnumerable<Point> points = segment switch
                {
                    LineSegment line => [line.Point],
                    PolyLineSegment poly => poly.Points,
                    _ => [],
                };

                foreach (Point point in points)
                {
                    length += (point - last).Length;
                    last = point;
                }
            }

            if (figure.IsClosed)
            {
                length += (figure.StartPoint - last).Length;
            }

            longest = Math.Max(longest, length);
        }

        return longest;
    }

    private static ScaleTransform EnsureScale(UIElement element)
    {
        if (element.RenderTransform is ScaleTransform { IsFrozen: false } existing)
        {
            return existing;
        }

        var scale = new ScaleTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        return scale;
    }
}
