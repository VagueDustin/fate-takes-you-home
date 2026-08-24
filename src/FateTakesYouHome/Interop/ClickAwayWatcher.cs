using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace FateTakesYouHome.Interop;

/// <summary>
/// Watches for a mouse press anywhere outside a set of rectangles, via a low-level mouse hook.
/// </summary>
/// <remarks>
/// <para>
/// A transient panel cannot rely on <c>Deactivated</c> alone: clicking the empty desktop, the
/// taskbar, or another app's non-activating surface moves no focus, raises no deactivation, and
/// leaves the panel standing. Every shell flyout — volume, network, clock — dismisses on the
/// <em>press</em>, wherever it lands, and this is the mechanism they use.
/// </para>
/// <para>
/// The hook callback runs on the installing thread's message pump and is kept trivial: two
/// rectangle tests against coordinates cached at install time (the panel does not move while it
/// is open). The actual dismissal is dispatched, never done inside the hook.
/// </para>
/// <para>
/// The hook exists only while the panel is visible. Installing a permanent low-level hook would
/// add latency to every mouse event on the machine for the lifetime of a tray app, which is the
/// kind of thing users rightly uninstall software over.
/// </para>
/// </remarks>
public sealed class ClickAwayWatcher : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;

    private readonly Dispatcher _dispatcher;

    // The delegate is a field so the GC cannot collect it while the hook holds its pointer.
    private readonly NativeMethods.LowLevelMouseProc _proc;

    private IntPtr _hook;
    private NativeMethods.RECT _inside;
    private NativeMethods.RECT _alsoInside;
    private bool _hasSecondRect;
    private Action? _onClickAway;

    public ClickAwayWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _proc = OnMouse;
    }

    /// <summary>True while the hook is installed.</summary>
    public bool IsWatching => _hook != IntPtr.Zero;

    /// <summary>
    /// Starts watching. Presses inside <paramref name="panel"/> — or inside
    /// <paramref name="exclusion"/>, typically the tray icon, whose click already toggles the
    /// panel through its own path — are ignored.
    /// </summary>
    /// <param name="panel">The panel rectangle, physical pixels.</param>
    /// <param name="exclusion">A second rectangle to leave alone, physical pixels.</param>
    /// <param name="onClickAway">Invoked on the dispatcher when a press lands anywhere else.</param>
    public void Start(Int32Rect panel, Int32Rect? exclusion, Action onClickAway)
    {
        Stop();

        _inside = ToRect(panel);
        _hasSecondRect = exclusion is not null;
        _alsoInside = exclusion is { } second ? ToRect(second) : default;
        _onClickAway = onClickAway;

        _hook = NativeMethods.SetWindowsHookEx(
            WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _onClickAway = null;
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int message = (int)wParam;

            if (message is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN)
            {
                NativeMethods.MSLLHOOKSTRUCT data =
                    Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

                bool inside = Contains(_inside, data.pt)
                              || (_hasSecondRect && Contains(_alsoInside, data.pt));

                if (!inside && _onClickAway is { } callback)
                {
                    _dispatcher.BeginInvoke(DispatcherPriority.Input, callback);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool Contains(NativeMethods.RECT rect, NativeMethods.POINT pt) =>
        pt.X >= rect.Left && pt.X < rect.Right && pt.Y >= rect.Top && pt.Y < rect.Bottom;

    private static NativeMethods.RECT ToRect(Int32Rect rect) => new()
    {
        Left = rect.X,
        Top = rect.Y,
        Right = rect.X + rect.Width,
        Bottom = rect.Y + rect.Height,
    };

    public void Dispose() => Stop();
}
