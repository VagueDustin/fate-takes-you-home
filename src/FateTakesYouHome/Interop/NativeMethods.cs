using System.Runtime.InteropServices;

namespace FateTakesYouHome.Interop;

/// <summary>
/// P/Invoke declarations. Grouped by the API surface they belong to rather than alphabetically,
/// because the constants only make sense next to the function that consumes them.
/// </summary>
internal static class NativeMethods
{
    // ==================================================================== window messages

    public const int WM_DESTROY = 0x0002;
    public const int WM_CLOSE = 0x0010;
    public const int WM_QUERYENDSESSION = 0x0011;
    public const int WM_ENDSESSION = 0x0016;
    public const int WM_ACTIVATE = 0x0006;
    public const int WM_ACTIVATEAPP = 0x001C;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONUP = 0x0208;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_THEMECHANGED = 0x031A;
    public const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
    public const int WM_DWMCOMPOSITIONCHANGED = 0x031E;

    /// <summary>First message id applications may define for themselves.</summary>
    public const int WM_APP = 0x8000;

    /// <summary>The callback message the tray icon sends us.</summary>
    public const int WM_TRAYICON = WM_APP + 0x21;

    // Shell notify-icon notifications, only sent under NOTIFYICON_VERSION_4.
    public const int NIN_SELECT = 0x0400;              // WM_USER + 0
    public const int NIN_KEYSELECT = 0x0401;           // WM_USER + 1
    public const int NIN_BALLOONSHOW = 0x0402;
    public const int NIN_BALLOONHIDE = 0x0403;
    public const int NIN_BALLOONTIMEOUT = 0x0404;
    public const int NIN_BALLOONUSERCLICK = 0x0405;
    public const int NIN_POPUPOPEN = 0x0406;
    public const int NIN_POPUPCLOSE = 0x0407;

    /// <summary>Parent handle that creates a message-only window.</summary>
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    // ==================================================================== shell notify icon

    public const int NIM_ADD = 0x00000000;
    public const int NIM_MODIFY = 0x00000001;
    public const int NIM_DELETE = 0x00000002;
    public const int NIM_SETFOCUS = 0x00000003;
    public const int NIM_SETVERSION = 0x00000004;

    public const int NIF_MESSAGE = 0x00000001;
    public const int NIF_ICON = 0x00000002;
    public const int NIF_TIP = 0x00000004;
    public const int NIF_STATE = 0x00000008;
    public const int NIF_INFO = 0x00000010;
    public const int NIF_GUID = 0x00000020;
    public const int NIF_SHOWTIP = 0x00000080;

    /// <summary>
    /// Version 4 gives us screen coordinates in the callback and the <c>NIN_*</c> notifications,
    /// which is what makes precise flyout placement possible.
    /// </summary>
    public const int NOTIFYICON_VERSION_4 = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public int dwState;
        public int dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        /// <summary>Union of uTimeout and uVersion. We only ever use it as uVersion.</summary>
        public int uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    /// <summary>
    /// Returns the screen rectangle the tray icon occupies.
    /// </summary>
    /// <remarks>
    /// This is the whole trick behind anchoring a flyout to its icon. It fails with
    /// <c>E_FAIL</c> when the icon is hidden in the overflow area, which the caller must handle by
    /// falling back to the notification area's own corner.
    /// </remarks>
    [DllImport("shell32.dll", SetLastError = false)]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public Guid guidItem;
    }

    // ==================================================================== app bar (taskbar)

    public const int ABM_GETTASKBARPOS = 0x00000005;
    public const int ABM_GETSTATE = 0x00000004;

    public const int ABS_AUTOHIDE = 0x0000001;
    public const int ABS_ALWAYSONTOP = 0x0000002;

    public const int ABE_LEFT = 0;
    public const int ABE_TOP = 1;
    public const int ABE_RIGHT = 2;
    public const int ABE_BOTTOM = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uCallbackMessage;
        public int uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

    // ==================================================================== monitors and DPI

    public const int MONITOR_DEFAULTTONULL = 0x00000000;
    public const int MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    public const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;

        public override readonly string ToString() => $"({Left},{Top})-({Right},{Bottom})";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromRect(ref RECT lprc, int dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    public const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>Per-monitor DPI. Available from Windows 8.1 onward.</summary>
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    // ==================================================================== window placement

    public const int SWP_NOSIZE = 0x0001;
    public const int SWP_NOMOVE = 0x0002;
    public const int SWP_NOZORDER = 0x0004;
    public const int SWP_NOACTIVATE = 0x0010;
    public const int SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    // ==================================================================== foreground rights

    /// <summary>
    /// Ties two threads' input queues together.
    /// </summary>
    /// <remarks>
    /// The documented workaround for <c>SetForegroundWindow</c> being refused. Windows only grants
    /// foreground to a process that received the last input event; when the user clicks a tray
    /// icon that process is Explorer, not us. Briefly attaching to Explorer's input queue makes the
    /// call succeed, which is the difference between a flyout that opens and one that flickers and
    /// vanishes.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    /// <summary>Grants another process the right to take the foreground.</summary>
    public const uint ASFW_ANY = 0xFFFFFFFF;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(uint dwProcessId);

    // ==================================================================== DWM

    /// <summary>Documented <c>DWMWINDOWATTRIBUTE</c> values used by this application.</summary>
    public enum DwmWindowAttribute
    {
        /// <summary>Paint the caption and border dark. Pre-20H1 builds used value 19.</summary>
        UseImmersiveDarkMode = 20,

        /// <summary>Round, round-small, or square corners. Windows 11 only.</summary>
        WindowCornerPreference = 33,

        BorderColor = 34,
        CaptionColor = 35,
        TextColor = 36,

        /// <summary>Mica, acrylic or tabbed backdrop. Windows 11 22H2 and later.</summary>
        SystemBackdropType = 38,
    }

    public enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3,
    }

    public enum DwmSystemBackdropType
    {
        Auto = 0,
        None = 1,

        /// <summary>Mica.</summary>
        MainWindow = 2,

        /// <summary>Acrylic.</summary>
        TransientWindow = 3,

        /// <summary>Mica Alt / tabbed.</summary>
        TabbedWindow = 4,
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    // ==================================================================== composition (acrylic)

    /// <summary>
    /// Undocumented but stable since Windows 10 1803. Used only to put acrylic behind a layered
    /// window, which the documented DWM backdrop API cannot do.
    /// </summary>
    /// <remarks>
    /// Every call site treats failure as "no acrylic" and falls back to the composited backdrop,
    /// so a future Windows release removing this degrades the look rather than breaking the app.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowCompositionAttribute(
        IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attrib;
        public IntPtr pvData;
        public int cbData;
    }

    /// <summary>WCA_ACCENT_POLICY.</summary>
    public const int WCA_ACCENT_POLICY = 19;

    [StructLayout(LayoutKind.Sequential)]
    public struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;

        /// <summary>Tint colour as AABBGGRR — note the byte order is reversed from Win32 COLORREF.</summary>
        public uint GradientColor;

        public int AnimationId;
    }

    public const int ACCENT_DISABLED = 0;
    public const int ACCENT_ENABLE_GRADIENT = 1;
    public const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;
    public const int ACCENT_ENABLE_BLURBEHIND = 3;
    public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    public const int ACCENT_ENABLE_HOSTBACKDROP = 5;

    // ==================================================================== system parameters

    /// <summary>
    /// Reports whether the user asked Windows to animate windows.
    /// </summary>
    /// <remarks>
    /// This is the closest Windows equivalent of the web's <c>prefers-reduced-motion</c>, and the
    /// brand contract requires honouring it. It reflects Settings &gt; Accessibility &gt; Visual
    /// effects &gt; Animation effects.
    /// </remarks>
    public const int SPI_GETCLIENTAREAANIMATION = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(
        int uiAction, int uiParam, ref bool pvParam, int fWinIni);

    // ==================================================================== icons

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegisterWindowMessage(string lpString);

    // ==================================================================== single instance

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    /// <summary>
    /// Builds an icon handle from raw icon-resource bits.
    /// </summary>
    /// <remarks>
    /// Passing <c>0x00030000</c> as the version selects the Windows 3.0 icon format, which is what
    /// every ICONIMAGE payload actually uses regardless of how new the OS is.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconFromResourceEx(
        IntPtr presbits, int dwResSize, [MarshalAs(UnmanagedType.Bool)] bool fIcon,
        int dwVer, int cxDesired, int cyDesired, int flags);

    public const int LR_DEFAULTCOLOR = 0x00000000;

    /// <summary>Version marker required by <see cref="CreateIconFromResourceEx"/>.</summary>
    public const int IconResourceVersion = 0x00030000;

    public const int SM_CXSMICON = 49;
    public const int SM_CYSMICON = 50;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    /// <summary>DPI-aware system metrics. Windows 10 1607 and later.</summary>
    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);
}
