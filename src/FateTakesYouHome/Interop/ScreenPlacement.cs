// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Windows;

namespace FateTakesYouHome.Interop;

/// <summary>Which screen edge the taskbar is docked to.</summary>
public enum TaskbarEdge
{
    Left,
    Top,
    Right,
    Bottom,
}

/// <summary>Where the taskbar is and how it behaves.</summary>
public sealed record TaskbarInfo(TaskbarEdge Edge, Int32Rect Bounds, bool IsAutoHide)
{
    /// <summary>A sensible guess for when the shell will not tell us. Bottom is the default on Windows.</summary>
    public static TaskbarInfo Unknown { get; } =
        new(TaskbarEdge.Bottom, new Int32Rect(0, 0, 0, 0), IsAutoHide: false);
}

/// <summary>A monitor's geometry, in physical pixels.</summary>
public sealed record MonitorGeometry(Int32Rect Bounds, Int32Rect WorkArea, uint Dpi)
{
    /// <summary>Device pixels per device-independent pixel.</summary>
    public double Scale => Dpi / 96d;
}

/// <summary>Where to put the flyout and which way it should travel as it opens.</summary>
public sealed record FlyoutPlacement(
    Int32Rect Bounds,
    TaskbarEdge Edge,
    double SlideOffsetX,
    double SlideOffsetY);

/// <summary>
/// Works out where a tray flyout belongs.
/// </summary>
/// <remarks>
/// Everything here is in physical pixels. WPF's <c>Window.Left</c>/<c>Top</c> are device-independent
/// and interpreted against the window's <em>current</em> monitor, which makes them unusable for
/// moving a window onto a monitor with a different scale factor — the classic symptom being a
/// flyout that lands half off-screen on a mixed-DPI desktop. Positioning through
/// <c>SetWindowPos</c> in physical pixels sidesteps the whole problem.
/// </remarks>
public static class ScreenPlacement
{
    /// <summary>Reads the taskbar's edge and rectangle from the shell.</summary>
    public static TaskbarInfo GetTaskbar()
    {
        var data = new NativeMethods.APPBARDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
        };

        if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref data) == IntPtr.Zero)
        {
            return TaskbarInfo.Unknown;
        }

        TaskbarEdge edge = data.uEdge switch
        {
            NativeMethods.ABE_LEFT => TaskbarEdge.Left,
            NativeMethods.ABE_TOP => TaskbarEdge.Top,
            NativeMethods.ABE_RIGHT => TaskbarEdge.Right,
            _ => TaskbarEdge.Bottom,
        };

        var state = new NativeMethods.APPBARDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
        };

        long flags = (long)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref state);
        bool autoHide = (flags & NativeMethods.ABS_AUTOHIDE) != 0;

        return new TaskbarInfo(
            edge,
            new Int32Rect(data.rc.Left, data.rc.Top, data.rc.Width, data.rc.Height),
            autoHide);
    }

    /// <summary>Finds the monitor containing a point, with its work area and DPI.</summary>
    public static MonitorGeometry GetMonitorFromPoint(int x, int y)
    {
        IntPtr handle = NativeMethods.MonitorFromPoint(
            new NativeMethods.POINT { X = x, Y = y },
            NativeMethods.MONITOR_DEFAULTTONEAREST);

        return DescribeMonitor(handle);
    }

    /// <summary>Finds the monitor a rectangle mostly occupies.</summary>
    public static MonitorGeometry GetMonitorFromRect(Int32Rect rect)
    {
        var native = new NativeMethods.RECT
        {
            Left = rect.X,
            Top = rect.Y,
            Right = rect.X + rect.Width,
            Bottom = rect.Y + rect.Height,
        };

        IntPtr handle = NativeMethods.MonitorFromRect(ref native, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return DescribeMonitor(handle);
    }

    private static MonitorGeometry DescribeMonitor(IntPtr handle)
    {
        var info = new NativeMethods.MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            szDevice = string.Empty,
        };

        if (handle == IntPtr.Zero || !NativeMethods.GetMonitorInfo(handle, ref info))
        {
            // Nothing sensible to fall back to but the virtual screen at system DPI.
            return new MonitorGeometry(
                new Int32Rect(0, 0, 1920, 1080),
                new Int32Rect(0, 0, 1920, 1040),
                96);
        }

        uint dpi = 96;
        try
        {
            if (NativeMethods.GetDpiForMonitor(handle, NativeMethods.MDT_EFFECTIVE_DPI, out uint x, out _) == 0
                && x > 0)
            {
                dpi = x;
            }
        }
        catch (DllNotFoundException)
        {
            // shcore.dll is absent before Windows 8.1. 96 is the only correct answer there.
        }

        return new MonitorGeometry(ToRect(info.rcMonitor), ToRect(info.rcWork), dpi);
    }

    private static Int32Rect ToRect(NativeMethods.RECT r) =>
        new(r.Left, r.Top, r.Width, r.Height);

    /// <summary>
    /// Places the flyout against the taskbar, anchored on the tray icon.
    /// </summary>
    /// <param name="anchor">
    /// The tray icon's rectangle in physical pixels. When the icon is hidden in the overflow area
    /// this will be the overflow button instead, which still points at the right corner.
    /// </param>
    /// <param name="sizePx">The flyout's size in physical pixels.</param>
    /// <param name="taskbar">Where the taskbar is docked.</param>
    /// <param name="monitor">The monitor the flyout should appear on.</param>
    /// <param name="marginPx">Gap between the flyout and the taskbar, in physical pixels.</param>
    public static FlyoutPlacement Compute(
        Int32Rect anchor,
        Int32Rect sizePx,
        TaskbarInfo taskbar,
        MonitorGeometry monitor,
        int marginPx)
    {
        int width = sizePx.Width;
        int height = sizePx.Height;

        Int32Rect work = monitor.WorkArea;

        // An auto-hidden taskbar leaves the work area covering the whole screen, so the flyout
        // would sit under the bar the moment it slides up. Reserve the bar's own thickness.
        int reserve = taskbar.IsAutoHide ? AutoHideReserve(taskbar) : 0;

        int x;
        int y;
        double slideX = 0;
        double slideY = 0;

        int anchorCentreX = anchor.X + (anchor.Width / 2);
        int anchorCentreY = anchor.Y + (anchor.Height / 2);

        switch (taskbar.Edge)
        {
            case TaskbarEdge.Bottom:
                x = anchorCentreX - (width / 2);
                y = work.Y + work.Height - reserve - marginPx - height;
                slideY = 1; // Travels upward, out of the bar.
                break;

            case TaskbarEdge.Top:
                x = anchorCentreX - (width / 2);
                y = work.Y + reserve + marginPx;
                slideY = -1;
                break;

            case TaskbarEdge.Left:
                x = work.X + reserve + marginPx;
                y = anchorCentreY - (height / 2);
                slideX = -1;
                break;

            default: // Right
                x = work.X + work.Width - reserve - marginPx - width;
                y = anchorCentreY - (height / 2);
                slideX = 1;
                break;
        }

        // Keep the whole flyout on the monitor. Clamping the far edge first and the near edge
        // second means a flyout taller than the work area is pinned to the top rather than
        // scrolled off it.
        x = Math.Min(x, work.X + work.Width - marginPx - width);
        x = Math.Max(x, work.X + marginPx);

        y = Math.Min(y, work.Y + work.Height - marginPx - height);
        y = Math.Max(y, work.Y + marginPx);

        return new FlyoutPlacement(new Int32Rect(x, y, width, height), taskbar.Edge, slideX, slideY);
    }

    /// <summary>The thickness to keep clear when the taskbar auto-hides.</summary>
    private static int AutoHideReserve(TaskbarInfo taskbar)
    {
        // The reported rectangle is the bar's real size even while it is hidden.
        int thickness = taskbar.Edge is TaskbarEdge.Left or TaskbarEdge.Right
            ? taskbar.Bounds.Width
            : taskbar.Bounds.Height;

        // A hidden bar reports a sliver; a shown one reports its full height. Either way the
        // reveal area is roughly the full bar, so clamp into a range that looks right for both.
        return Math.Clamp(thickness, 2, 80);
    }

    /// <summary>Rounds a device-independent length up to whole physical pixels.</summary>
    public static int ToPhysical(double dips, double scale) =>
        (int)Math.Ceiling(dips * scale);
}
