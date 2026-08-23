using System.Runtime.InteropServices;
using System.Windows.Media;
using FateTakesYouHome.Theming.Model;

namespace FateTakesYouHome.Interop;

/// <summary>
/// How a window's corners are drawn.
/// </summary>
/// <remarks>
/// Mirrors <c>DWM_WINDOW_CORNER_PREFERENCE</c>. Declared separately because the P/Invoke layer is
/// internal and must not leak into a public signature.
/// </remarks>
public enum WindowCorner
{
    /// <summary>Whatever Windows would do on its own.</summary>
    Default,

    Square,
    Round,
    RoundSmall,
}

/// <summary>
/// Applies the window effects Windows offers, and reports honestly when it will not.
/// </summary>
/// <remarks>
/// Every method here returns a bool rather than throwing. These are all presentation niceties on
/// APIs whose availability varies by Windows build, and none of them is worth failing a window
/// over — the caller falls back to painting the surface itself.
/// </remarks>
public static class WindowEffects
{
    /// <summary>Windows 11 21H2. Below this, corner and backdrop attributes are ignored.</summary>
    private const int Windows11Build = 22000;

    /// <summary>Windows 11 22H2, where <c>DWMWA_SYSTEMBACKDROP_TYPE</c> became documented.</summary>
    private const int Windows11_22H2Build = 22621;

    /// <summary>True on Windows 11 or later.</summary>
    public static bool IsWindows11 { get; } =
        Environment.OSVersion.Version.Build >= Windows11Build;

    /// <summary>True where the documented system backdrop attribute is honoured.</summary>
    public static bool SupportsSystemBackdrop { get; } =
        Environment.OSVersion.Version.Build >= Windows11_22H2Build;

    /// <summary>
    /// Tells the DWM to draw this window's non-client area dark.
    /// </summary>
    /// <remarks>
    /// Without this a dark-themed window gets a white title bar, which is the single most obvious
    /// tell that an app is not really following the system theme.
    /// </remarks>
    public static bool SetDarkMode(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        int value = dark ? 1 : 0;

        int hr = NativeMethods.DwmSetWindowAttribute(
            hwnd,
            (int)NativeMethods.DwmWindowAttribute.UseImmersiveDarkMode,
            ref value,
            sizeof(int));

        if (hr == 0)
        {
            return true;
        }

        // Windows 10 builds between 17763 and 18362 used attribute 19 for the same thing.
        const int legacyDarkModeAttribute = 19;
        return NativeMethods.DwmSetWindowAttribute(hwnd, legacyDarkModeAttribute, ref value, sizeof(int)) == 0;
    }

    /// <summary>Rounds the window's corners. Windows 11 only; a no-op elsewhere.</summary>
    public static bool SetCornerPreference(IntPtr hwnd, WindowCorner corner)
    {
        if (hwnd == IntPtr.Zero || !IsWindows11)
        {
            return false;
        }

        int value = (int)(corner switch
        {
            WindowCorner.Square => NativeMethods.DwmWindowCornerPreference.DoNotRound,
            WindowCorner.Round => NativeMethods.DwmWindowCornerPreference.Round,
            WindowCorner.RoundSmall => NativeMethods.DwmWindowCornerPreference.RoundSmall,
            _ => NativeMethods.DwmWindowCornerPreference.Default,
        });

        return NativeMethods.DwmSetWindowAttribute(
            hwnd,
            (int)NativeMethods.DwmWindowAttribute.WindowCornerPreference,
            ref value,
            sizeof(int)) == 0;
    }

    /// <summary>
    /// Requests a mica or acrylic system backdrop.
    /// </summary>
    /// <remarks>
    /// Only works on a window that is <em>not</em> layered — that is, one created with
    /// <c>AllowsTransparency="False"</c>. A WPF window with transparency enabled is composited by
    /// WPF itself and the DWM has nothing to draw behind.
    /// </remarks>
    public static bool SetSystemBackdrop(IntPtr hwnd, BackdropMode mode)
    {
        if (hwnd == IntPtr.Zero || !SupportsSystemBackdrop)
        {
            return false;
        }

        NativeMethods.DwmSystemBackdropType type = mode switch
        {
            BackdropMode.Mica => NativeMethods.DwmSystemBackdropType.MainWindow,
            BackdropMode.Acrylic => NativeMethods.DwmSystemBackdropType.TransientWindow,
            _ => NativeMethods.DwmSystemBackdropType.None,
        };

        int value = (int)type;

        if (NativeMethods.DwmSetWindowAttribute(
                hwnd,
                (int)NativeMethods.DwmWindowAttribute.SystemBackdropType,
                ref value,
                sizeof(int)) != 0)
        {
            return false;
        }

        // The backdrop only shows through where the frame has been extended into the client area.
        var margins = new NativeMethods.MARGINS
        {
            cxLeftWidth = -1,
            cxRightWidth = -1,
            cyTopHeight = -1,
            cyBottomHeight = -1,
        };

        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        return true;
    }

    /// <summary>
    /// Puts an acrylic blur behind a layered window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This uses <c>SetWindowCompositionAttribute</c>, which is undocumented but has behaved
    /// consistently since Windows 10 1803. It is the only way to get a blur behind a window that
    /// WPF is compositing itself, which the documented DWM backdrop cannot do.
    /// </para>
    /// <para>
    /// Failure is expected and handled: the caller keeps the theme's own translucent tint, which
    /// looks deliberate rather than broken.
    /// </para>
    /// </remarks>
    public static bool TrySetAcrylic(IntPtr hwnd, Color tint, double opacity)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        byte alpha = (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);

        // The gradient colour is ABGR, not the ARGB or COLORREF byte order used elsewhere in Win32.
        uint gradient = ((uint)alpha << 24)
                      | ((uint)tint.B << 16)
                      | ((uint)tint.G << 8)
                      | tint.R;

        var policy = new NativeMethods.ACCENT_POLICY
        {
            AccentState = NativeMethods.ACCENT_ENABLE_ACRYLICBLURBEHIND,

            // Flag 2 draws the tint across the whole client area rather than only the border.
            AccentFlags = 2,
            GradientColor = gradient,
            AnimationId = 0,
        };

        int size = Marshal.SizeOf<NativeMethods.ACCENT_POLICY>();
        IntPtr buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(policy, buffer, fDeleteOld: false);

            var data = new NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
            {
                Attrib = NativeMethods.WCA_ACCENT_POLICY,
                pvData = buffer,
                cbData = size,
            };

            return NativeMethods.SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch (EntryPointNotFoundException)
        {
            // The export is gone. Nothing to do but let the caller paint its own background.
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Removes any composition accent previously applied to the window.</summary>
    public static void ClearAcrylic(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var policy = new NativeMethods.ACCENT_POLICY
        {
            AccentState = NativeMethods.ACCENT_DISABLED,
        };

        int size = Marshal.SizeOf<NativeMethods.ACCENT_POLICY>();
        IntPtr buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(policy, buffer, fDeleteOld: false);

            var data = new NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
            {
                Attrib = NativeMethods.WCA_ACCENT_POLICY,
                pvData = buffer,
                cbData = size,
            };

            NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch (EntryPointNotFoundException)
        {
            // Nothing was applied in the first place.
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Hides the window from Alt+Tab and the taskbar.</summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(
            hwnd, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Whether the user has asked Windows to stop animating windows.
    /// </summary>
    /// <remarks>
    /// The Windows counterpart of <c>prefers-reduced-motion</c>. The brand contract requires
    /// honouring it, and every animation in this application is decorative, so there is nothing
    /// lost by doing so.
    /// </remarks>
    public static bool IsReducedMotionPreferred()
    {
        bool animationsEnabled = true;

        if (!NativeMethods.SystemParametersInfo(
                NativeMethods.SPI_GETCLIENTAREAANIMATION, 0, ref animationsEnabled, 0))
        {
            // Could not ask. Assume animation is wanted rather than silently disabling it.
            return false;
        }

        return !animationsEnabled;
    }
}
