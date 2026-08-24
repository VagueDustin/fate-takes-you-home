using System.Windows;
using System.Windows.Controls;

namespace FateTakesYouHome.Controls;

/// <summary>
/// The widget grid: children are placed by cell coordinates and the cells stretch with the width.
/// </summary>
/// <remarks>
/// <para>
/// Columns are fixed in number and fluid in size, phone-launcher style: a widget spanning two of
/// six columns is a third of the surface whatever the window measures. Rows have a fixed height,
/// because content — a tile row, a chart — has a natural height that should not balloon on a big
/// monitor.
/// </para>
/// <para>
/// Placement comes from the attached <c>Cell*</c> properties, which the item container style binds
/// to the widget's view model. Changing them re-arranges; nothing is rebuilt.
/// </para>
/// </remarks>
public sealed class WidgetCanvas : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(WidgetCanvas),
        new FrameworkPropertyMetadata(6, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(
        nameof(RowHeight), typeof(double), typeof(WidgetCanvas),
        new FrameworkPropertyMetadata(84.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(WidgetCanvas),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty CellXProperty = DependencyProperty.RegisterAttached(
        "CellX", typeof(int), typeof(WidgetCanvas),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty CellYProperty = DependencyProperty.RegisterAttached(
        "CellY", typeof(int), typeof(WidgetCanvas),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty CellWProperty = DependencyProperty.RegisterAttached(
        "CellW", typeof(int), typeof(WidgetCanvas),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty CellHProperty = DependencyProperty.RegisterAttached(
        "CellH", typeof(int), typeof(WidgetCanvas),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public static void SetCellX(DependencyObject element, int value) => element.SetValue(CellXProperty, value);
    public static int GetCellX(DependencyObject element) => (int)element.GetValue(CellXProperty);
    public static void SetCellY(DependencyObject element, int value) => element.SetValue(CellYProperty, value);
    public static int GetCellY(DependencyObject element) => (int)element.GetValue(CellYProperty);
    public static void SetCellW(DependencyObject element, int value) => element.SetValue(CellWProperty, value);
    public static int GetCellW(DependencyObject element) => (int)element.GetValue(CellWProperty);
    public static void SetCellH(DependencyObject element, int value) => element.SetValue(CellHProperty, value);
    public static int GetCellH(DependencyObject element) => (int)element.GetValue(CellHProperty);

    /// <summary>The width of one column at the panel's current size.</summary>
    public double ColumnWidth =>
        Columns > 0 ? Math.Max(1, (ActualWidth + Gap) / Columns) : 1;

    private Rect RectFor(UIElement child, double columnWidth)
    {
        int x = Math.Max(0, GetCellX(child));
        int y = Math.Max(0, GetCellY(child));
        int w = Math.Max(1, GetCellW(child));
        int h = Math.Max(1, GetCellH(child));

        double left = x * columnWidth;
        double top = y * (RowHeight + Gap);
        double width = Math.Max(1, (w * columnWidth) - Gap);
        double height = Math.Max(1, (h * (RowHeight + Gap)) - Gap);

        return new Rect(left, top, width, height);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        double columnWidth = Columns > 0 ? Math.Max(1, (width + Gap) / Columns) : 1;
        int rows = 0;

        foreach (UIElement child in InternalChildren)
        {
            Rect rect = RectFor(child, columnWidth);
            child.Measure(new Size(rect.Width, rect.Height));
            rows = Math.Max(rows, GetCellY(child) + Math.Max(1, GetCellH(child)));
        }

        double height = rows > 0 ? (rows * (RowHeight + Gap)) - Gap : 0;
        return new Size(width, Math.Max(0, height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double columnWidth = Columns > 0 ? Math.Max(1, (finalSize.Width + Gap) / Columns) : 1;

        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(RectFor(child, columnWidth));
        }

        return finalSize;
    }

    /// <summary>The cell under a point, for drag placement. Clamped into the grid.</summary>
    public (int X, int Y) CellAt(Point point)
    {
        double columnWidth = ColumnWidth;
        int x = Math.Clamp((int)Math.Floor(point.X / columnWidth), 0, Math.Max(0, Columns - 1));
        int y = Math.Max(0, (int)Math.Floor(point.Y / (RowHeight + Gap)));
        return (x, y);
    }
}
