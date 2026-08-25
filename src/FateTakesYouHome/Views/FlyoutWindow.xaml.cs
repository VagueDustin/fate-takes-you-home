// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FateTakesYouHome.Animation;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.Interop;
using FateTakesYouHome.Services;
using FateTakesYouHome.Theming.Model;
using FateTakesYouHome.ViewModels;

namespace FateTakesYouHome.Views;

/// <summary>
/// The tray panel.
/// </summary>
/// <remarks>
/// <para>
/// The window is created once and reused. It is never actually closed until the application exits —
/// hiding it and showing it again is what keeps the panel appearing instantly, and rebuilding a
/// WPF window costs enough to be visible at this size.
/// </para>
/// <para>
/// Positioning goes through <c>SetWindowPos</c> in physical pixels rather than
/// <c>Window.Left</c>/<c>Top</c>. On a mixed-DPI desktop those properties are interpreted against
/// the window's current monitor, so setting them to move a window <em>to</em> a different monitor
/// puts it in the wrong place — the bug where a flyout lands half off the screen.
/// </para>
/// </remarks>
public partial class FlyoutWindow : Window
{
    private readonly AppLog _log;
    private readonly SettingsService _settings;
    private readonly ThemeService _themes;
    private readonly HomeAssistantService _homeAssistant;
    private readonly FlyoutViewModel _viewModel;

    /// <summary>
    /// How long after showing to disregard a deactivation.
    /// </summary>
    /// <remarks>
    /// Long enough to cover the show-position-activate sequence and Explorer letting go of the
    /// foreground, short enough that a genuine click elsewhere still dismisses the panel promptly.
    /// </remarks>
    private static readonly TimeSpan ActivationGrace = TimeSpan.FromMilliseconds(450);

    private HwndSource? _source;
    private readonly ClickAwayWatcher _clickAway;
    private bool _closingForReal;
    private bool _isAnimatingOut;
    private DateTime _ignoreDeactivateUntil = DateTime.MinValue;

    public FlyoutWindow(
        AppLog log,
        SettingsService settings,
        ThemeService themes,
        HomeAssistantService homeAssistant)
    {
        _log = log;
        _settings = settings;
        _themes = themes;
        _homeAssistant = homeAssistant;

        InitializeComponent();

        _viewModel = new FlyoutViewModel(settings, homeAssistant);
        DataContext = _viewModel;

        _viewModel.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _viewModel.ExpandRequested += (_, _) => ExpandRequested?.Invoke(this, EventArgs.Empty);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _themes.Applied += OnThemeApplied;

        _clickAway = new ClickAwayWatcher(Dispatcher);

        Deactivated += OnDeactivated;
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>
    /// Supplies the tray icon's rectangle, so a click on the icon is left to the icon's own
    /// toggle handling rather than double-dismissed by the click-away watcher.
    /// </summary>
    public Func<Int32Rect?>? TrayIconRectProvider { get; set; }

    /// <summary>Raised when the user asks for the settings page.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the user asks for the full window.</summary>
    public event EventHandler? ExpandRequested;

    /// <summary>True while the panel is on screen and not mid-dismissal.</summary>
    public bool IsOpen => IsVisible && !_isAnimatingOut;

    // ------------------------------------------------------------------ showing

    /// <summary>
    /// Positions the panel against the taskbar, anchored on <paramref name="anchor"/>, and plays
    /// it in.
    /// </summary>
    public void ShowAnchoredTo(Int32Rect anchor)
    {
        Theme theme = _themes.Current;

        _isAnimatingOut = false;

        // Cancel a dismissal that is still running, or the two animations fight.
        ThemedMotion.Snap(this, OpacityProperty, 0);

        Width = theme.Shape.FlyoutWidth;
        TileScroller.MaxHeight = theme.Shape.FlyoutMaxHeight;

        // The window has to exist and be laid out before it can be measured, but showing it at
        // its default position would flash in the wrong corner. Opacity zero covers the gap.
        Opacity = 0;

        if (!IsVisible)
        {
            Show();
        }

        // Force a full measure so ActualHeight reflects the current pin list rather than whatever
        // was in it last time.
        UpdateLayout();

        FlyoutPlacement placement = Place(anchor, theme);
        Move(placement);

        // Everything between Show and here can produce a transient deactivation: Explorer still
        // holds the foreground because the click went to it, and the reposition can shuffle
        // activation. Guard the window against dismissing itself while it is still arriving.
        _ignoreDeactivateUntil = DateTime.UtcNow + ActivationGrace;

        // Deactivation alone cannot dismiss the panel: clicking the bare desktop or the taskbar
        // activates nothing, so no event ever arrives. The watcher sees the press itself.
        if (!_settings.Current.PinFlyoutOpen)
        {
            _clickAway.Start(placement.Bounds, TrayIconRectProvider?.Invoke(), OnClickedAway);
        }

        TakeForeground();
        PlayEntrance(placement, theme);
    }

    private void OnClickedAway()
    {
        if (_settings.Current.PinFlyoutOpen)
        {
            _clickAway.Stop();
            return;
        }

        HideAnimated();
    }

    /// <summary>
    /// Works out where the panel goes, in physical pixels.
    /// </summary>
    /// <remarks>
    /// Placement is computed for the <em>visible panel</em>, not for the window. The window is
    /// larger by the shadow frame on every side, and treating that empty margin as part of the
    /// panel would push the panel a shadow-width away from the taskbar — a theme asking for a
    /// 12px gap would silently get 28. The window rectangle is derived afterwards by growing the
    /// panel placement, so the shadow simply overhangs, which is exactly what a shadow should do.
    /// </remarks>
    private FlyoutPlacement Place(Int32Rect anchor, Theme theme)
    {
        TaskbarInfo taskbar = ScreenPlacement.GetTaskbar();

        MonitorGeometry monitor = anchor.Width > 0
            ? ScreenPlacement.GetMonitorFromRect(anchor)
            : ScreenPlacement.GetMonitorFromPoint(anchor.X, anchor.Y);

        // The window's own DPI may differ from the target monitor's until Windows moves it, so
        // the conversion uses the destination's scale rather than the current one.
        double scale = monitor.Scale;

        double shadow = ShadowFrame.Margin.Left;

        int panelWidthPx = ScreenPlacement.ToPhysical(Width, scale);
        int panelHeightPx = ScreenPlacement.ToPhysical(Math.Max(0, ActualHeight - (shadow * 2)), scale);
        int marginPx = ScreenPlacement.ToPhysical(theme.Shape.FlyoutMargin, scale);

        return ScreenPlacement.Compute(
            anchor,
            new Int32Rect(0, 0, panelWidthPx, panelHeightPx),
            taskbar,
            monitor,
            marginPx);
    }

    /// <summary>Positions the window around the panel placement, allowing for the shadow frame.</summary>
    private void Move(FlyoutPlacement placement)
    {
        if (_source?.Handle is not { } handle || handle == IntPtr.Zero)
        {
            return;
        }

        MonitorGeometry monitor = ScreenPlacement.GetMonitorFromRect(placement.Bounds);
        int shadowPx = ScreenPlacement.ToPhysical(ShadowFrame.Margin.Left, monitor.Scale);

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            placement.Bounds.X - shadowPx,
            placement.Bounds.Y - shadowPx,
            placement.Bounds.Width + (shadowPx * 2),
            placement.Bounds.Height + (shadowPx * 2),
            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Plays the panel out of the taskbar edge: it fades, grows and travels in the one direction
    /// that makes sense for where the taskbar is.
    /// </summary>
    private void PlayEntrance(FlyoutPlacement placement, Theme theme)
    {
        ThemeMotion motion = theme.Motion;
        double target = theme.Backdrop.FlyoutOpacity;

        if (!motion.Enabled)
        {
            Opacity = target;
            Slide.X = 0;
            Slide.Y = 0;
            Zoom.ScaleX = 1;
            Zoom.ScaleY = 1;
            return;
        }

        // Travel starts on the taskbar side and resolves to zero, so the panel reads as emerging
        // from the icon rather than appearing next to it.
        Slide.X = placement.SlideOffsetX * motion.FlyoutTravel;
        Slide.Y = placement.SlideOffsetY * motion.FlyoutTravel;

        Zoom.ScaleX = motion.FlyoutScaleFrom;
        Zoom.ScaleY = motion.FlyoutScaleFrom;

        // Grow from the corner nearest the tray icon, not from the middle.
        ShadowFrame.RenderTransformOrigin = OriginFor(placement.Edge);

        ThemedMotion.AnimateDouble(this, OpacityProperty, target, MotionSpeed.FlyoutOpen);
        ThemedMotion.AnimateDouble(Slide, TranslateTransform.XProperty, 0, MotionSpeed.FlyoutOpen);
        ThemedMotion.AnimateDouble(Slide, TranslateTransform.YProperty, 0, MotionSpeed.FlyoutOpen);
        ThemedMotion.AnimateDouble(Zoom, ScaleTransform.ScaleXProperty, 1, MotionSpeed.FlyoutOpen);
        ThemedMotion.AnimateDouble(Zoom, ScaleTransform.ScaleYProperty, 1, MotionSpeed.FlyoutOpen);
    }

    /// <summary>The corner the panel should grow from, given where the taskbar is.</summary>
    private static Point OriginFor(TaskbarEdge edge) => edge switch
    {
        TaskbarEdge.Bottom => new Point(0.5, 1.0),
        TaskbarEdge.Top => new Point(0.5, 0.0),
        TaskbarEdge.Left => new Point(0.0, 0.5),
        _ => new Point(1.0, 0.5),
    };

    // ------------------------------------------------------------------ hiding

    /// <summary>Fades the panel out and then hides it.</summary>
    public void HideAnimated()
    {
        if (!IsVisible || _isAnimatingOut)
        {
            return;
        }

        _isAnimatingOut = true;
        _clickAway.Stop();

        if (!_themes.Current.Motion.Enabled)
        {
            FinishHiding();
            return;
        }

        ThemedMotion.AnimateDouble(this, OpacityProperty, 0, MotionSpeed.FlyoutClose, FinishHiding);
    }

    private void FinishHiding()
    {
        // A second Show() may have started while the fade was running.
        if (!_isAnimatingOut)
        {
            return;
        }

        _isAnimatingOut = false;
        _ignoreDeactivateUntil = DateTime.MinValue;
        Hide();
    }

    /// <summary>
    /// Dismisses the panel when focus genuinely moves elsewhere.
    /// </summary>
    /// <remarks>
    /// A deactivation arriving during the grace window is not the user clicking away — it is
    /// Explorer still holding the foreground from the tray click. Hiding on it is what made the
    /// panel flash and vanish. Inside the window the foreground is re-asserted instead.
    /// </remarks>
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_settings.Current.PinFlyoutOpen)
        {
            return;
        }

        if (DateTime.UtcNow < _ignoreDeactivateUntil)
        {
            // Try again for the foreground; if it never arrives the panel simply stays open until
            // the next click, which is far better than closing itself immediately.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(TakeForeground));
            return;
        }

        HideAnimated();
    }

    /// <summary>Claims the foreground, working around the restriction on doing so.</summary>
    private void TakeForeground()
    {
        if (!IsVisible)
        {
            return;
        }

        if (_source?.Handle is { } handle && handle != IntPtr.Zero)
        {
            WindowEffects.ForceForeground(handle);
        }

        Activate();
        Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideAnimated();
        }
    }

    // ------------------------------------------------------------------ window plumbing

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource)PresentationSource.FromVisual(this)!;

        // Keep the panel out of Alt+Tab and off the taskbar. It is a transient surface, not a
        // window somebody should have to manage.
        WindowEffects.MakeToolWindow(_source.Handle);

        ApplyBackdrop();
    }

    /// <summary>
    /// Applies whatever backdrop the theme asked for, falling back quietly when Windows says no.
    /// </summary>
    private void ApplyBackdrop()
    {
        if (_source?.Handle is not { } handle || handle == IntPtr.Zero)
        {
            return;
        }

        Theme theme = _themes.Current;

        WindowEffects.SetDarkMode(handle, theme.Appearance == ThemeAppearance.Dark);

        switch (theme.Backdrop.Mode)
        {
            case BackdropMode.Acrylic:
                // The window is layered because AllowsTransparency is on, so the documented DWM
                // backdrop will not apply. The composition attribute is the only route — but it
                // paints the accent across the whole window RECTANGLE, transparent pixels
                // included. With the shadow frame in place that meant a dark slab around the
                // panel. So in acrylic mode the frame collapses to nothing, the WPF shadow is
                // retired, and DWM rounds the actual window to match the panel.
                if (WindowEffects.TrySetAcrylic(
                        handle, theme.Colors.SurfaceBase, theme.Backdrop.TintOpacity))
                {
                    ShadowFrame.Margin = new Thickness(0);
                    Panel.Effect = null;
                    WindowEffects.SetCornerPreference(handle, WindowCorner.Round);
                }
                else
                {
                    _log.Debug("Acrylic was refused; keeping the composited background.");
                    RestoreCompositedFrame(handle);
                }

                break;

            case BackdropMode.Mica:
                _log.Debug(
                    "Mica cannot be applied to a transparent window. The panel keeps its own "
                    + "background; set backdrop.mode to \"acrylic\" for a blur.");
                WindowEffects.ClearAcrylic(handle);
                RestoreCompositedFrame(handle);
                break;

            default:
                WindowEffects.ClearAcrylic(handle);
                RestoreCompositedFrame(handle);
                break;
        }
    }

    /// <summary>Puts back the shadow frame a previous acrylic application removed.</summary>
    private void RestoreCompositedFrame(IntPtr handle)
    {
        ShadowFrame.Margin = new Thickness(CompositedShadowMargin);
        Panel.SetResourceReference(EffectProperty, "Fate.Effect.PanelShadow");
        WindowEffects.SetCornerPreference(handle, WindowCorner.Default);
    }

    /// <summary>The room the drop shadow renders into around the composited panel.</summary>
    private const double CompositedShadowMargin = 28;

    private void OnThemeApplied(object? sender, ThemeAppliedEventArgs e)
    {
        Width = e.Theme.Shape.FlyoutWidth;
        TileScroller.MaxHeight = e.Theme.Shape.FlyoutMaxHeight;

        ApplyBackdrop();
        UpdateStatusVisuals();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FlyoutViewModel.ConnectionState)
            or nameof(FlyoutViewModel.IsConnected)
            or nameof(FlyoutViewModel.StatusMessage))
        {
            UpdateStatusVisuals();
        }
    }

    /// <summary>
    /// Colours the header's status dot.
    /// </summary>
    /// <remarks>
    /// Connected is deliberately <em>not</em> gold. The brand reserves the accent for things you
    /// can press; a status indicator painted in it reads as a button.
    /// </remarks>
    private void UpdateStatusVisuals()
    {
        string brushKey = _viewModel.ConnectionState switch
        {
            HaConnectionState.Connected => Theming.Rendering.ThemeKeys.BrushStatusSuccess,
            HaConnectionState.Connecting or HaConnectionState.Authenticating =>
                Theming.Rendering.ThemeKeys.BrushStatusInfo,
            HaConnectionState.Reconnecting => Theming.Rendering.ThemeKeys.BrushStatusWarning,
            HaConnectionState.Failed => Theming.Rendering.ThemeKeys.BrushStatusDanger,
            _ => Theming.Rendering.ThemeKeys.BrushTextFaint,
        };

        StatusDot.SetResourceReference(BackgroundProperty, brushKey);

        StatusText.Text = _viewModel.ConnectionState == HaConnectionState.Connected
            ? _viewModel.HomeName
            : _viewModel.StatusMessage;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Only the application shutting down may actually destroy this window; everything else
        // that "closes" the panel just hides it.
        if (!_closingForReal)
        {
            e.Cancel = true;
            HideAnimated();
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>Really closes the window. Used at shutdown and when the backdrop mode changes.</summary>
    public new void Close()
    {
        _closingForReal = true;

        _themes.Applied -= OnThemeApplied;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();

        base.Close();
    }
}
