using System.Windows;
using System.Windows.Media;

namespace FateTakesYouHome.Controls;

/// <summary>
/// Draws short L-shaped rules at the corners of its bounds.
/// </summary>
/// <remarks>
/// A charted- and ceremonial-tier device from the brand contract. The brackets imply a frame
/// without drawing one, which is what keeps a gilded panel from reading as a plain box. They are
/// purely decorative, so the element is hit-test invisible and is skipped entirely at the utility
/// tier.
/// </remarks>
public sealed class CornerBrackets : FrameworkElement
{
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(CornerBrackets),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(CornerBrackets),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Length of each arm, in device-independent pixels.</summary>
    public static readonly DependencyProperty ArmLengthProperty = DependencyProperty.Register(
        nameof(ArmLength),
        typeof(double),
        typeof(CornerBrackets),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Distance from the bounds to the bracket, in device-independent pixels.</summary>
    public static readonly DependencyProperty InsetProperty = DependencyProperty.Register(
        nameof(Inset),
        typeof(double),
        typeof(CornerBrackets),
        new FrameworkPropertyMetadata(6d, FrameworkPropertyMetadataOptions.AffectsRender));

    public CornerBrackets()
    {
        // Decoration must never intercept a click meant for the content underneath.
        IsHitTestVisible = false;
        Focusable = false;
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double ArmLength
    {
        get => (double)GetValue(ArmLengthProperty);
        set => SetValue(ArmLengthProperty, value);
    }

    public double Inset
    {
        get => (double)GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        double arm = ArmLength;
        double inset = Inset;

        // Nothing sensible to draw if the brackets would meet or overlap in the middle.
        if (width <= (inset + arm) * 2 || height <= (inset + arm) * 2)
        {
            return;
        }

        var pen = new Pen(Stroke, StrokeThickness)
        {
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat,
        };
        pen.Freeze();

        // Half-pixel offset puts a one-pixel stroke on the pixel rather than across two of them.
        double offset = StrokeThickness / 2;

        double left = inset + offset;
        double top = inset + offset;
        double right = width - inset - offset;
        double bottom = height - inset - offset;

        // Top-left
        drawingContext.DrawLine(pen, new Point(left, top), new Point(left + arm, top));
        drawingContext.DrawLine(pen, new Point(left, top), new Point(left, top + arm));

        // Top-right
        drawingContext.DrawLine(pen, new Point(right - arm, top), new Point(right, top));
        drawingContext.DrawLine(pen, new Point(right, top), new Point(right, top + arm));

        // Bottom-left
        drawingContext.DrawLine(pen, new Point(left, bottom - arm), new Point(left, bottom));
        drawingContext.DrawLine(pen, new Point(left, bottom), new Point(left + arm, bottom));

        // Bottom-right
        drawingContext.DrawLine(pen, new Point(right - arm, bottom), new Point(right, bottom));
        drawingContext.DrawLine(pen, new Point(right, bottom - arm), new Point(right, bottom));
    }
}
