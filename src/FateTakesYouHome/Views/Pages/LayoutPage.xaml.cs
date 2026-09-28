// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using FateTakesYouHome.Animation;
using FateTakesYouHome.Controls;
using FateTakesYouHome.Theming.Rendering;
using FateTakesYouHome.ViewModels;

namespace FateTakesYouHome.Views.Pages;

/// <summary>
/// The layout editor's interaction layer: drag to move, pull the corner to resize.
/// </summary>
/// <remarks>
/// <para>
/// The page owns the mouse mechanics and <see cref="GridReflow"/> owns the rules. During a drag
/// the other widgets make way live, sliding aside as the card passes over them, and an outline
/// shows the cells it will settle into. The card itself is lifted above the others and follows the
/// pointer by the pixel, within the range it could actually land in, rather than jumping from cell
/// to cell. On release it glides into its cell.
/// </para>
/// <para>
/// The following is a render transform on top of the arranged position, never a change to the
/// layout, so a card in flight costs one transform per mouse move and nothing is re-measured.
/// Every animation goes through <see cref="ThemedMotion"/>, which turns it into an instant change
/// when the theme, the user or Windows has asked for reduced motion.
/// </para>
/// </remarks>
public partial class LayoutPage : UserControl
{
    /// <summary>How close to the top or bottom of the editor, in pixels, dragging starts to scroll.</summary>
    public const double ScrollEdge = 48;

    /// <summary>The fastest the editor scrolls while dragging, in pixels per tick, reached at the very edge.</summary>
    public const double MaxScrollStep = 22;

    /// <summary>How much a card grows as it is picked up: enough to read as lifted, not enough to cover its neighbours.</summary>
    private const double LiftScale = 1.03;

    private enum DragMode
    {
        None,

        /// <summary>Pressed, but not yet moved far enough to count as a drag. A click is not a drag.</summary>
        Pending,

        Move,
        Resize,
    }

    private readonly DispatcherTimer _scrollTimer;

    private DragMode _mode = DragMode.None;
    private DragMode _requested = DragMode.None;
    private EditorWidget? _widget;
    private WidgetCanvas? _canvas;
    private FrameworkElement? _container;
    private Point _pressPoint;
    private Vector _grabOffset;
    private GridCell[] _startCells = [];
    private int _movedIndex;
    private GridCell _target;
    private int _floor;
    private TranslateTransform? _follow;
    private ScaleTransform? _lift;

    public LayoutPage()
    {
        InitializeComponent();

        // Mouse moves stop arriving when the pointer rests at the edge, which is exactly when the
        // scrolling should continue, so the scroll runs on a timer rather than on mouse moves.
        _scrollTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _scrollTimer.Tick += OnScrollTick;
    }

    private LayoutEditorViewModel? ViewModel => DataContext as LayoutEditorViewModel;

    /// <summary>
    /// How far to scroll this tick for a pointer at <paramref name="pointerY"/> within a viewport
    /// of <paramref name="viewportHeight"/>: nothing in the middle, faster the deeper into an edge
    /// band, and full speed once past the edge.
    /// </summary>
    public static double AutoScrollStep(double pointerY, double viewportHeight)
    {
        if (viewportHeight <= 0)
        {
            return 0;
        }

        // In a short editor the bands would meet in the middle; keep a still zone between them.
        double edge = Math.Min(ScrollEdge, viewportHeight / 4);

        if (pointerY < edge)
        {
            return -MaxScrollStep * Math.Min(1, (edge - pointerY) / edge);
        }

        if (pointerY > viewportHeight - edge)
        {
            return MaxScrollStep * Math.Min(1, (pointerY - (viewportHeight - edge)) / edge);
        }

        return 0;
    }

    // ------------------------------------------------------------------ grabbing

    private void OnWidgetGrabbed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EditorWidget widget })
        {
            return;
        }

        BeginDrag(widget, DragMode.Move, e);
    }

    private void OnGripGrabbed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EditorWidget widget })
        {
            return;
        }

        BeginDrag(widget, DragMode.Resize, e);
        e.Handled = true;
    }

    private void BeginDrag(EditorWidget widget, DragMode mode, MouseButtonEventArgs e)
    {
        _canvas = FindCanvas(EditorHost);

        if (_canvas is null || ViewModel is not { } viewModel)
        {
            return;
        }

        _widget = widget;
        _container = EditorHost.ItemContainerGenerator.ContainerFromItem(widget) as FrameworkElement;
        _mode = DragMode.Pending;
        _requested = mode;
        _startCells = viewModel.Cells();
        _movedIndex = viewModel.Items.IndexOf(widget);
        _target = _startCells[_movedIndex];
        _floor = LayoutEditorViewModel.DeepestRow(widget, viewModel.Items);
        _pressPoint = e.GetPosition(_canvas);
        _grabOffset = _pressPoint - _canvas.CellRect(widget.X, widget.Y, widget.W, widget.H).TopLeft;

        EditorHost.CaptureMouse();
    }

    // ------------------------------------------------------------------ dragging

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_mode == DragMode.None || _canvas is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishDrag();
            return;
        }

        Point pointer = e.GetPosition(_canvas);

        if (_mode == DragMode.Pending)
        {
            Vector moved = pointer - _pressPoint;

            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _mode = _requested;
            Lift();
            _scrollTimer.Start();
        }

        Track(pointer);
    }

    /// <summary>Moves or resizes the widget to follow a pointer position on the canvas.</summary>
    private void Track(Point pointer)
    {
        if (_widget is not { } widget || _canvas is not { } canvas || ViewModel is not { } viewModel)
        {
            return;
        }

        int columns = viewModel.Columns;

        if (_mode == DragMode.Move)
        {
            // The card is drawn only where it could land: a full-width card slides up and down and
            // never sideways, and nothing is drawn above the grid or past the row beneath the rest.
            Point wanted = pointer - _grabOffset;
            var topLeft = new Point(
                Math.Clamp(wanted.X, 0, Math.Max(0, columns - widget.W) * canvas.ColumnWidth),
                Math.Clamp(wanted.Y, 0, _floor * canvas.RowPitch));

            (int x, int y) = WidgetCanvas.NearestCell(topLeft, canvas.ColumnWidth, canvas.RowPitch);
            Rearrange(new GridCell(
                Math.Clamp(x, 0, Math.Max(0, columns - widget.W)),
                Math.Clamp(y, 0, _floor),
                widget.W,
                widget.H));

            // The container is arranged at the widget's cell; the transform carries the card the
            // rest of the way to the pointer.
            if (_follow is not null)
            {
                Vector offset = topLeft - canvas.CellRect(widget.X, widget.Y, widget.W, widget.H).TopLeft;
                _follow.X = offset.X;
                _follow.Y = offset.Y;
            }
        }
        else if (_mode == DragMode.Resize)
        {
            (int cellX, int cellY) = canvas.CellAt(pointer);
            Rearrange(_target with
            {
                W = Math.Clamp(cellX - _target.X + 1, 1, Math.Max(1, columns - _target.X)),
                H = Math.Clamp(cellY - _target.Y + 1, 1, 6),
            });
        }

        ShowDropSlot(widget, canvas);
    }

    /// <summary>
    /// Arranges the grid for the dragged widget at <paramref name="target"/>, sliding every other
    /// widget that has to move from where it was drawn to where it now belongs.
    /// </summary>
    /// <param name="settle">True on drop, when the dragged widget may float up into a gap too.</param>
    private void Rearrange(GridCell target, bool settle = false)
    {
        if (ViewModel is not { } viewModel || _canvas is not { } canvas)
        {
            return;
        }

        // Nothing to do until the pointer reaches a different cell; mouse moves within one cell
        // only move the card, not the grid.
        if (!settle && target == _target)
        {
            return;
        }

        _target = target;

        GridCell[] before = viewModel.Cells();
        var shownAt = new Point[before.Length];

        for (int i = 0; i < before.Length; i++)
        {
            shownAt[i] = canvas.CellRect(before[i].X, before[i].Y, before[i].W, before[i].H).TopLeft
                         + CurrentNudge(i);
        }

        GridCell[] after = GridReflow.Arrange(_startCells, _movedIndex, target, settle);
        viewModel.Apply(after);

        for (int i = 0; i < after.Length; i++)
        {
            if (i != _movedIndex && (after[i].X, after[i].Y) != (before[i].X, before[i].Y))
            {
                Nudge(i, shownAt[i] - canvas.CellRect(after[i].X, after[i].Y, after[i].W, after[i].H).TopLeft);
            }
        }
    }

    /// <summary>
    /// How far a widget is currently drawn from its cell: by a slide still in progress, or by the
    /// glide of a card that was dropped a moment ago and has not landed yet.
    /// </summary>
    private Vector CurrentNudge(int index) =>
        ContainerAt(index)?.RenderTransform is { } transform
            ? new Vector(transform.Value.OffsetX, transform.Value.OffsetY)
            : default;

    /// <summary>
    /// Slides a widget that has just been given a new cell from where it was drawn into that cell,
    /// so neighbours making way are seen to move rather than blinking into place.
    /// </summary>
    private void Nudge(int index, Vector from)
    {
        if (ContainerAt(index) is not { } container)
        {
            return;
        }

        if (container.RenderTransform is not TranslateTransform slide || slide.IsFrozen)
        {
            slide = new TranslateTransform();
            container.RenderTransform = slide;
        }

        // A card still gliding in from the last drop loses that glide here, and with it the chance
        // to put its shadow away when it lands. It is being moved as a neighbour now, not carried.
        container.Effect = null;
        Panel.SetZIndex(container, 0);

        slide.BeginAnimation(TranslateTransform.XProperty, null);
        slide.BeginAnimation(TranslateTransform.YProperty, null);
        slide.X = from.X;
        slide.Y = from.Y;

        ThemedMotion.AnimateDouble(slide, TranslateTransform.XProperty, 0, MotionSpeed.FlyoutClose);
        ThemedMotion.AnimateDouble(slide, TranslateTransform.YProperty, 0, MotionSpeed.FlyoutClose);
    }

    private FrameworkElement? ContainerAt(int index) =>
        ViewModel is { } viewModel && index >= 0 && index < viewModel.Items.Count
            ? EditorHost.ItemContainerGenerator.ContainerFromItem(viewModel.Items[index]) as FrameworkElement
            : null;

    private void OnScrollTick(object? sender, EventArgs e)
    {
        if (_mode is not (DragMode.Move or DragMode.Resize) || _canvas is null)
        {
            _scrollTimer.Stop();
            return;
        }

        double step = AutoScrollStep(Mouse.GetPosition(EditorScroll).Y, EditorScroll.ActualHeight);

        if (step == 0)
        {
            return;
        }

        double before = EditorScroll.VerticalOffset;
        EditorScroll.ScrollToVerticalOffset(before + step);
        EditorScroll.UpdateLayout();

        // The canvas moved under a still pointer, so the card has to follow as if it had moved.
        if (EditorScroll.VerticalOffset != before)
        {
            Track(Mouse.GetPosition(_canvas));
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e) => FinishDrag();

    /// <summary>Ends a drag cleanly if the capture is taken away, by another window or a switch of app.</summary>
    private void OnCanvasLostMouseCapture(object sender, MouseEventArgs e) => FinishDrag();

    private void FinishDrag()
    {
        if (_mode == DragMode.None)
        {
            return;
        }

        DragMode ending = _mode;
        bool wasDragging = ending is DragMode.Move or DragMode.Resize;

        // Cleared before releasing capture, because releasing it raises LostMouseCapture, which
        // lands back here.
        _mode = DragMode.None;
        _scrollTimer.Stop();
        EditorHost.ReleaseMouseCapture();
        DropSlot.Visibility = Visibility.Collapsed;

        if (wasDragging && _widget is { } widget && _canvas is { } canvas && ViewModel is { } viewModel)
        {
            // Where the card is drawn right now, before its cell changes under it.
            Point shownAt = canvas.CellRect(widget.X, widget.Y, widget.W, widget.H).TopLeft
                            + new Vector(_follow?.X ?? 0, _follow?.Y ?? 0);

            // A moved card may float up into a gap on release. A resized one stays on its row:
            // the person changed its size, not where it is.
            Rearrange(_target, settle: ending == DragMode.Move);

            if (GridReflow.HasOverlap(viewModel.Cells()))
            {
                // Cannot happen on a grid that was sound when the drag began, but a hand-edited
                // settings file can start one that is not. Leave it as it was rather than worse.
                viewModel.Apply(_startCells);
            }
            else if (!viewModel.Cells().SequenceEqual(_startCells))
            {
                viewModel.MarkDirty();
            }

            Settle(canvas.CellRect(widget.X, widget.Y, widget.W, widget.H).TopLeft, shownAt);
        }

        _widget = null;
        _canvas = null;
        _container = null;
        _follow = null;
        _lift = null;
    }

    // ------------------------------------------------------------------ lift and settle

    /// <summary>Raises the card above the others, with the theme's panel shadow, and grows it slightly.</summary>
    private void Lift()
    {
        if (_container is not { } container)
        {
            return;
        }

        Panel.SetZIndex(container, 1);
        container.Effect = TryFindResource(ThemeKeys.EffectPanelShadow) as Effect;

        // Only a moving card follows the pointer. A resizing one stays anchored at its corner,
        // and growing it would fight the size the pointer is setting.
        if (_mode != DragMode.Move)
        {
            return;
        }

        _lift = new ScaleTransform(1, 1);
        _follow = new TranslateTransform();

        container.RenderTransformOrigin = new Point(0.5, 0.5);
        container.RenderTransform = new TransformGroup { Children = { _lift, _follow } };

        ThemedMotion.AnimateDouble(_lift, ScaleTransform.ScaleXProperty, LiftScale, MotionSpeed.Press);
        ThemedMotion.AnimateDouble(_lift, ScaleTransform.ScaleYProperty, LiftScale, MotionSpeed.Press);
    }

    /// <summary>
    /// Glides the card from where it was shown to the cell it now occupies, then puts it back
    /// among the others.
    /// </summary>
    private void Settle(Point home, Point shownAt)
    {
        if (_container is not { } container)
        {
            return;
        }

        Transform settling = container.RenderTransform;

        void Land()
        {
            // Picked up again before it landed: the new drag has lifted it and will settle it.
            if (ReferenceEquals(container, _container) && _mode is DragMode.Move or DragMode.Resize)
            {
                return;
            }

            container.Effect = null;
            Panel.SetZIndex(container, 0);

            // Nudged since, as a neighbour of a newer drag: that slide owns the transform now.
            if (ReferenceEquals(container.RenderTransform, settling))
            {
                container.RenderTransform = Transform.Identity;
            }
        }

        if (_follow is not { } follow || _lift is not { } lift)
        {
            container.Effect = null;
            Panel.SetZIndex(container, 0);
            return;
        }

        // The cell may just have changed (a blocked drop going home), so re-express the card's
        // current position relative to its new cell before gliding that offset away to nothing.
        Vector from = shownAt - home;
        follow.BeginAnimation(TranslateTransform.XProperty, null);
        follow.BeginAnimation(TranslateTransform.YProperty, null);
        follow.X = from.X;
        follow.Y = from.Y;

        // The flyout's easing is the theme's springiest, which is what a card dropping into
        // place should feel like.
        ThemedMotion.AnimateDouble(follow, TranslateTransform.XProperty, 0, MotionSpeed.FlyoutOpen, Land);
        ThemedMotion.AnimateDouble(follow, TranslateTransform.YProperty, 0, MotionSpeed.FlyoutOpen);
        ThemedMotion.AnimateDouble(lift, ScaleTransform.ScaleXProperty, 1, MotionSpeed.Press);
        ThemedMotion.AnimateDouble(lift, ScaleTransform.ScaleYProperty, 1, MotionSpeed.Press);
    }

    /// <summary>Outlines the cells the dragged card will settle into, beneath the card itself.</summary>
    private void ShowDropSlot(EditorWidget widget, WidgetCanvas canvas)
    {
        // A resizing card already is its own outline.
        if (_mode != DragMode.Move)
        {
            DropSlot.Visibility = Visibility.Collapsed;
            return;
        }

        Rect cells = canvas.CellRect(widget.X, widget.Y, widget.W, widget.H);
        Point topLeft = canvas.TranslatePoint(cells.TopLeft, DragLayer);

        Canvas.SetLeft(DropSlot, topLeft.X);
        Canvas.SetTop(DropSlot, topLeft.Y);
        DropSlot.Width = cells.Width;
        DropSlot.Height = cells.Height;

        DropSlot.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------ picker

    private void OnPickerDoubleClick(object sender, MouseButtonEventArgs e) =>
        ViewModel?.ConfirmPickCommand.Execute(null);

    private static WidgetCanvas? FindCanvas(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);

            if (child is WidgetCanvas canvas)
            {
                return canvas;
            }

            if (FindCanvas(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
