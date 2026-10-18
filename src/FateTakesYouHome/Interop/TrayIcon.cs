// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace FateTakesYouHome.Interop;

/// <summary>Where the pointer was when the tray icon was clicked, in physical screen pixels.</summary>
public sealed class TrayClickEventArgs : EventArgs
{
    public required int ScreenX { get; init; }

    public required int ScreenY { get; init; }
}

/// <summary>
/// A notification-area icon, driven directly through <c>Shell_NotifyIcon</c>.
/// </summary>
/// <remarks>
/// <para>
/// WPF has no tray icon, and the WinForms <c>NotifyIcon</c> cannot report where it is on screen.
/// This implementation exists for <see cref="TryGetIconRect"/>: anchoring the flyout to the icon
/// the user actually clicked (rather than to the corner of the screen) is the difference between
/// feeling native and feeling approximate.
/// </para>
/// <para>
/// The icon is registered against a hidden top-level window, and re-registers itself when Explorer
/// restarts. Not a message-only window: those never receive the <c>TaskbarCreated</c> broadcast.
/// </para>
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    /// <summary>Identifies this icon within our own window. Constant; we only ever show one.</summary>
    private const int IconId = 1;

    private readonly HwndSource _messageWindow;
    private readonly int _taskbarCreatedMessage;
    private readonly Action<string, Exception?>? _log;

    private IntPtr _iconHandle;
    private string _tooltip = string.Empty;
    private bool _added;
    private bool _disposed;

    public TrayIcon(Action<string, Exception?>? log = null)
    {
        _log = log;

        // A real (invisible) top-level window, like the single-instance listener's. A message-only
        // window is left out of broadcasts, so it never hears TaskbarCreated and the icon is gone
        // for good after Explorer restarts.
        var parameters = new HwndSourceParameters("FateTakesYouHome.TrayIcon")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP: no frame, and never visible.
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW, // Stays off the taskbar.
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
        };

        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);

        // Explorer restarting destroys every tray icon and then broadcasts this. Without handling
        // it, the app silently loses its icon whenever Explorer crashes or is restarted.
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
    }

    /// <summary>Single click, or Enter while the icon has keyboard focus.</summary>
    public event EventHandler<TrayClickEventArgs>? Selected;

    /// <summary>Double click.</summary>
    public event EventHandler<TrayClickEventArgs>? DoubleClicked;

    /// <summary>Right click, or the context-menu key.</summary>
    public event EventHandler<TrayClickEventArgs>? ContextMenuRequested;

    /// <summary>Middle click. Used for the "run my default action" shortcut.</summary>
    public event EventHandler<TrayClickEventArgs>? MiddleClicked;

    public IntPtr WindowHandle => _messageWindow.Handle;

    public bool IsVisible => _added;

    /// <summary>Adds the icon to the notification area.</summary>
    public void Show(BitmapSource icon, string tooltip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _tooltip = Truncate(tooltip, 127);
        SetIconHandle(icon);

        if (_added)
        {
            Modify();
            return;
        }

        Add();
    }

    /// <summary>Replaces the icon image, for example to reflect the connection state.</summary>
    public void UpdateIcon(BitmapSource icon)
    {
        if (_disposed)
        {
            return;
        }

        SetIconHandle(icon);
        Modify();
    }

    /// <summary>Replaces the hover tooltip.</summary>
    public void UpdateTooltip(string tooltip)
    {
        if (_disposed)
        {
            return;
        }

        _tooltip = Truncate(tooltip, 127);
        Modify();
    }

    /// <summary>
    /// Shows a notification from the icon: a toast on current Windows, a balloon on older.
    /// </summary>
    /// <remarks>
    /// Silent, and held back during the quiet time Windows gives a newly signed-in account. It only
    /// ever reports something that did not happen, to somebody who has just pressed a key and is
    /// already looking for the result, so a sound would add nothing but noise.
    /// </remarks>
    public void ShowNotification(string title, string text)
    {
        if (_disposed || !_added)
        {
            return;
        }

        NativeMethods.NOTIFYICONDATA data = BuildData(NativeMethods.NIF_INFO);
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags =
            NativeMethods.NIIF_WARNING | NativeMethods.NIIF_NOSOUND | NativeMethods.NIIF_RESPECT_QUIET_TIME;

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data))
        {
            _log?.Invoke("Could not show a notification from the tray icon.", null);
        }
    }

    /// <summary>
    /// Returns the icon's rectangle in physical screen pixels.
    /// </summary>
    /// <remarks>
    /// Fails when the icon is hidden in the overflow flyout, which is the normal state for a
    /// freshly installed app. Callers must have a fallback rather than treating this as reliable.
    /// </remarks>
    public bool TryGetIconRect(out Int32Rect rect)
    {
        rect = default;

        if (!_added)
        {
            return false;
        }

        var identifier = new NativeMethods.NOTIFYICONIDENTIFIER
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
            hWnd = _messageWindow.Handle,
            uID = IconId,
            guidItem = Guid.Empty,
        };

        int hr = NativeMethods.Shell_NotifyIconGetRect(ref identifier, out NativeMethods.RECT native);

        // S_FALSE (1) means the icon is in the overflow area; the rectangle returned is the
        // overflow button's, which is still a better anchor than nothing.
        if (hr != 0 && hr != 1)
        {
            return false;
        }

        if (native.Width <= 0 || native.Height <= 0)
        {
            return false;
        }

        rect = new Int32Rect(native.Left, native.Top, native.Width, native.Height);
        return true;
    }

    // ------------------------------------------------------------------ message handling

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            OnExplorerRestarted();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg != NativeMethods.WM_TRAYICON)
        {
            return IntPtr.Zero;
        }

        // Under version 4 the notification is in the low word of lParam and the cursor position
        // is packed into wParam as two signed 16-bit values.
        int notification = LowWord(lParam);
        int x = SignedLowWord(wParam);
        int y = SignedHighWord(wParam);

        var args = new TrayClickEventArgs { ScreenX = x, ScreenY = y };

        switch (notification)
        {
            case NativeMethods.NIN_SELECT:
            case NativeMethods.NIN_KEYSELECT:
            case NativeMethods.WM_LBUTTONUP:
                Selected?.Invoke(this, args);
                handled = true;
                break;

            case NativeMethods.WM_LBUTTONDBLCLK:
                DoubleClicked?.Invoke(this, args);
                handled = true;
                break;

            case NativeMethods.WM_CONTEXTMENU:
            case NativeMethods.WM_RBUTTONUP:
                ContextMenuRequested?.Invoke(this, args);
                handled = true;
                break;

            case NativeMethods.WM_MBUTTONUP:
                MiddleClicked?.Invoke(this, args);
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void OnExplorerRestarted()
    {
        _log?.Invoke("Explorer restarted; re-adding the tray icon.", null);

        // The old registration died with the previous Explorer instance.
        _added = false;

        // Also the retry for an icon that could not be added at sign-in, since Show keeps the icon
        // image whether or not the add worked.
        if (_iconHandle != IntPtr.Zero)
        {
            Add();
        }
    }

    private void Add()
    {
        NativeMethods.NOTIFYICONDATA data = BuildData(
            NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);

        // The add fails while the notification area is not up yet at sign-in; TaskbarCreated
        // retries it. It also fails when the taskbar was only rebuilt, as after a DPI change, and
        // the icon is still there, in which case modifying it is what works.
        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data)
            && !NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data))
        {
            _log?.Invoke(
                "Could not add the tray icon. The notification area may not be ready yet; "
                + "it will be added when it announces itself.", null);
            return;
        }

        _added = true;

        // Opting in to version 4 changes the callback to deliver screen coordinates and the
        // NIN_* notifications. Everything about placement depends on this succeeding.
        NativeMethods.NOTIFYICONDATA version = BuildData(0);
        version.uVersion = NativeMethods.NOTIFYICON_VERSION_4;

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref version))
        {
            _log?.Invoke("Tray icon is running in legacy mode; placement may be less precise.", null);
        }
    }

    // ------------------------------------------------------------------ plumbing

    private NativeMethods.NOTIFYICONDATA BuildData(int flags) => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _messageWindow.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = NativeMethods.WM_TRAYICON,
        hIcon = _iconHandle,
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void Modify()
    {
        if (!_added)
        {
            return;
        }

        NativeMethods.NOTIFYICONDATA data = BuildData(
            NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);

        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private void SetIconHandle(BitmapSource icon)
    {
        ArgumentNullException.ThrowIfNull(icon);

        IntPtr previous = _iconHandle;
        _iconHandle = IconInterop.CreateHIcon(icon);

        // Destroyed only after the replacement is in place, so the shell never sees a null icon.
        if (previous != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(previous);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) ? string.Empty
        : value.Length <= maxLength ? value
        : value[..maxLength];

    private static int LowWord(IntPtr value) => (int)((long)value & 0xFFFF);

    private static int SignedLowWord(IntPtr value) => (short)((long)value & 0xFFFF);

    private static int SignedHighWord(IntPtr value) => (short)(((long)value >> 16) & 0xFFFF);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_added)
        {
            NativeMethods.NOTIFYICONDATA data = BuildData(0);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }

        _messageWindow.RemoveHook(WndProc);
        _messageWindow.Dispose();
    }
}
