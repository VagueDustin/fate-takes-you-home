// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FateTakesYouHome.Controls;
using FateTakesYouHome.ViewModels;

namespace FateTakesYouHome.Views.Pages;

/// <summary>
/// The layout editor's interaction layer: drag to move, pull the corner to resize.
/// </summary>
/// <remarks>
/// The page owns the mouse mechanics and the view model owns the rules. During a drag the cells
/// update live so the canvas re-arranges under the cursor, exactly the way a phone launcher
/// behaves; a drop that would overlap simply snaps back to where the drag began.
/// </remarks>
public partial class LayoutPage : UserControl
{
    private enum DragMode
    {
        None,
        Move,
        Resize,
    }

    private DragMode _mode = DragMode.None;
    private EditorWidget? _widget;
    private WidgetCanvas? _canvas;
    private (int X, int Y) _grabOffset;
    private (int X, int Y, int W, int H) _start;

    public LayoutPage()
    {
        InitializeComponent();
    }

    private LayoutEditorViewModel? ViewModel => DataContext as LayoutEditorViewModel;

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

        if (_canvas is null)
        {
            return;
        }

        _widget = widget;
        _mode = mode;
        _start = (widget.X, widget.Y, widget.W, widget.H);

        (int cellX, int cellY) = _canvas.CellAt(e.GetPosition(_canvas));
        _grabOffset = (cellX - widget.X, cellY - widget.Y);

        EditorHost.CaptureMouse();
    }

    // ------------------------------------------------------------------ dragging

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_mode == DragMode.None || _widget is null || _canvas is null
            || ViewModel is not { } viewModel)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishDrag();
            return;
        }

        (int cellX, int cellY) = _canvas.CellAt(e.GetPosition(_canvas));
        int columns = viewModel.Columns;

        if (_mode == DragMode.Move)
        {
            _widget.X = Math.Clamp(cellX - _grabOffset.X, 0, Math.Max(0, columns - _widget.W));
            _widget.Y = Math.Max(0, cellY - _grabOffset.Y);
        }
        else
        {
            _widget.W = Math.Clamp(cellX - _widget.X + 1, 1, columns - _widget.X);
            _widget.H = Math.Clamp(cellY - _widget.Y + 1, 1, 6);
        }

        _widget.IsInvalid = viewModel.CollidesWithAnything(_widget);
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e) => FinishDrag();

    private void FinishDrag()
    {
        EditorHost.ReleaseMouseCapture();

        if (_widget is { } widget)
        {
            if (widget.IsInvalid)
            {
                // An overlapping drop snaps home rather than shoving neighbours around.
                (widget.X, widget.Y, widget.W, widget.H) = _start;
                widget.IsInvalid = false;
            }
            else if ((widget.X, widget.Y, widget.W, widget.H) != _start)
            {
                ViewModel?.MarkDirty();
            }
        }

        _mode = DragMode.None;
        _widget = null;
        _canvas = null;
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
