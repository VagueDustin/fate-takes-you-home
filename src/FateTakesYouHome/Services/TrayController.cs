using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FateTakesYouHome.Branding;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.Interop;
using FateTakesYouHome.Models;
using FateTakesYouHome.Views;

namespace FateTakesYouHome.Services;

/// <summary>Which page the full window should open on.</summary>
public enum MainWindowSection
{
    Welcome,
    Dashboard,
    Entities,
    Themes,
    Settings,
    Help,
}

/// <summary>
/// Owns the notification icon and everything reachable from it.
/// </summary>
/// <remarks>
/// <para>
/// Single click opens the flyout immediately rather than waiting to see whether a double click is
/// coming. Waiting out <c>GetDoubleClickTime</c> — around half a second — before showing anything
/// would make the primary interaction feel broken. A double click therefore opens the flyout and
/// then supersedes it with the full window, which is the trade every good tray app makes.
/// </para>
/// <para>
/// The flyout window is created once and reused. Building it per click costs enough to be visible,
/// and the whole point is that it appears the instant it is asked for.
/// </para>
/// </remarks>
public sealed class TrayController : IDisposable
{
    private readonly AppLog _log;
    private readonly SettingsService _settings;
    private readonly ThemeService _themes;
    private readonly HomeAssistantService _homeAssistant;

    private TrayIcon? _icon;
    private FlyoutWindow? _flyout;
    private MainWindow? _mainWindow;
    private ContextMenu? _menu;
    private uint _iconDpi;
    private bool _disposed;

    public TrayController(
        AppLog log,
        SettingsService settings,
        ThemeService themes,
        HomeAssistantService homeAssistant)
    {
        _log = log;
        _settings = settings;
        _themes = themes;
        _homeAssistant = homeAssistant;
    }

    /// <summary>Adds the icon and wires everything to it.</summary>
    public void Start()
    {
        _icon = new TrayIcon(_log.AsCallback());
        _icon.Selected += OnSelected;
        _icon.DoubleClicked += OnDoubleClicked;
        _icon.ContextMenuRequested += OnContextMenuRequested;
        _icon.MiddleClicked += OnMiddleClicked;

        RefreshIcon();

        _homeAssistant.PropertyChanged += OnHomeAssistantPropertyChanged;
        _themes.Applied += OnThemeApplied;

        _log.Info("Tray icon added.");
    }

    // ------------------------------------------------------------------ tray gestures

    private void OnSelected(object? sender, TrayClickEventArgs e) => ToggleFlyout(e);

    private void OnDoubleClicked(object? sender, TrayClickEventArgs e) =>
        Perform(_settings.Current.TrayDoubleClickAction, e);

    private void OnMiddleClicked(object? sender, TrayClickEventArgs e) =>
        Perform(_settings.Current.TrayMiddleClickAction, e);

    private void Perform(TrayAction action, TrayClickEventArgs e)
    {
        switch (action)
        {
            case TrayAction.ToggleFlyout:
                ToggleFlyout(e);
                break;

            case TrayAction.OpenMainWindow:
                // A double click has already opened the flyout on its first click. Close it so
                // the two do not end up fighting for the foreground.
                HideFlyout();
                ShowMainWindow();
                break;

            case TrayAction.RunDefaultAction:
                _ = RunDefaultActionAsync();
                break;

            case TrayAction.None:
            default:
                break;
        }
    }

    /// <summary>Shows the flyout anchored to the tray icon, or hides it if already open.</summary>
    public void ToggleFlyout(TrayClickEventArgs? click = null)
    {
        if (_flyout is { IsOpen: true })
        {
            HideFlyout();
            return;
        }

        ShowFlyout(click);
    }

    public void ShowFlyout(TrayClickEventArgs? click = null)
    {
        if (_disposed || _icon is null)
        {
            return;
        }

        FlyoutWindow flyout = EnsureFlyout();

        // Prefer the icon's own rectangle. It fails when the icon is in the overflow area, in
        // which case the click coordinates are the next best anchor.
        Int32Rect anchor;

        if (_icon.TryGetIconRect(out Int32Rect iconRect))
        {
            anchor = iconRect;
        }
        else if (click is not null)
        {
            anchor = new Int32Rect(click.ScreenX, click.ScreenY, 1, 1);
        }
        else
        {
            TaskbarInfo bar = ScreenPlacement.GetTaskbar();
            anchor = new Int32Rect(
                bar.Bounds.X + bar.Bounds.Width, bar.Bounds.Y, 1, bar.Bounds.Height);
        }

        flyout.ShowAnchoredTo(anchor);
    }

    public void HideFlyout() => _flyout?.HideAnimated();

    private FlyoutWindow EnsureFlyout()
    {
        if (_flyout is not null)
        {
            return _flyout;
        }

        _flyout = new FlyoutWindow(_log, _settings, _themes, _homeAssistant);
        _flyout.SettingsRequested += (_, _) =>
        {
            HideFlyout();
            ShowMainWindow(MainWindowSection.Settings);
        };
        _flyout.ExpandRequested += (_, _) =>
        {
            HideFlyout();
            ShowMainWindow(MainWindowSection.Dashboard);
        };

        return _flyout;
    }

    /// <summary>Rebuilds the flyout, for when a theme change altered something baked into the window.</summary>
    private void RecreateFlyout()
    {
        if (_flyout is null)
        {
            return;
        }

        _log.Debug("Backdrop mode changed; rebuilding the flyout window.");

        FlyoutWindow old = _flyout;
        _flyout = null;

        old.Close();
    }

    // ------------------------------------------------------------------ main window

    /// <summary>Opens the full window, bringing it forward if it is already open.</summary>
    public void ShowMainWindow(MainWindowSection section = MainWindowSection.Dashboard)
    {
        if (_disposed)
        {
            return;
        }

        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(_log, _settings, _themes, _homeAssistant, this);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.Navigate(section);

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
        _mainWindow.Focus();
    }

    // ------------------------------------------------------------------ context menu

    private void OnContextMenuRequested(object? sender, TrayClickEventArgs e)
    {
        if (_icon is null)
        {
            return;
        }

        // Without this the menu will not dismiss when the user clicks elsewhere: Win32 only
        // auto-closes a menu owned by the foreground window, and ours is a message-only window
        // that has never been foreground.
        NativeMethods.SetForegroundWindow(_icon.WindowHandle);

        _menu ??= BuildContextMenu();

        // MousePoint placement lets WPF resolve the screen position itself, which avoids doing
        // the physical-to-device-independent conversion by hand on a multi-DPI desktop.
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu
        {
            StaysOpen = false,
        };

        if (Application.Current.TryFindResource("Fate.ContextMenu") is Style menuStyle)
        {
            menu.Style = menuStyle;
        }

        menu.Items.Add(MenuItemFor("Open", () => ShowMainWindow(), isDefault: true));
        menu.Items.Add(MenuItemFor("Show panel", () => ShowFlyout()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("Reconnect", () => _ = _homeAssistant.ReconnectAsync()));
        menu.Items.Add(MenuItemFor("Settings…", () => ShowMainWindow(MainWindowSection.Settings)));
        menu.Items.Add(MenuItemFor("Help", () => ShowMainWindow(MainWindowSection.Help)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("Exit", () => App.Current?.RequestShutdown()));

        return menu;
    }

    private static MenuItem MenuItemFor(string header, Action action, bool isDefault = false)
    {
        var item = new MenuItem
        {
            Header = header,
            FontWeight = isDefault ? FontWeights.SemiBold : FontWeights.Normal,
        };

        if (Application.Current.TryFindResource("Fate.MenuItem") is Style style)
        {
            item.Style = style;
        }

        item.Click += (_, _) => action();
        return item;
    }

    // ------------------------------------------------------------------ default action

    /// <summary>Fires whichever pinned entity is marked as the default action.</summary>
    public async Task RunDefaultActionAsync()
    {
        PinnedEntity? pin = _settings.Current.Pinned.FirstOrDefault(p => p.IsDefaultAction);

        if (pin is null)
        {
            _log.Info("A tray gesture asked for the default action, but no pin is marked as one.");
            return;
        }

        HomeAssistant.Models.HaEntityState? state = _homeAssistant.Find(pin.EntityId);

        if (state is null)
        {
            _log.Warning($"The default action points at '{pin.EntityId}', which no longer exists.");
            return;
        }

        CommandResult result = await _homeAssistant
            .ExecuteAsync(
                (client, ct) => client.ToggleAsync(state, ct),
                $"Default action on {pin.EntityId}")
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            _log.Warning($"The default action failed: {result.ErrorMessage}");
        }
    }

    // ------------------------------------------------------------------ icon state

    private void OnHomeAssistantPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeAssistantService.ConnectionState)
            or nameof(HomeAssistantService.StatusMessage)
            or nameof(HomeAssistantService.LocationName))
        {
            RefreshIcon();
        }
    }

    private void OnThemeApplied(object? sender, ThemeAppliedEventArgs e)
    {
        Animation.ThemedMotion.ResetBudget();

        // The context menu caches its style; rebuild it so the next open picks up the new theme.
        _menu = null;

        if (e.BackdropModeChanged)
        {
            RecreateFlyout();
        }
    }

    /// <summary>Redraws the tray icon and updates its tooltip.</summary>
    private void RefreshIcon()
    {
        if (_icon is null || _disposed)
        {
            return;
        }

        try
        {
            uint dpi = CurrentTrayDpi();
            int size = IconInterop.GetTrayIconSize(dpi);

            bool connected = _homeAssistant.ConnectionState == HaConnectionState.Connected;

            // Never plated: notification-area icons are glyphs on transparency, and a plated one
            // would sit in a little box while every neighbour does not. Disconnected is drawn
            // flat rather than greyed, so it reads as "not live" without becoming invisible.
            BitmapSource bitmap = FateMark.Render(size, withPlate: false, monochrome: !connected);

            if (_icon.IsVisible)
            {
                _icon.UpdateIcon(bitmap);
            }
            else
            {
                _icon.Show(bitmap, BuildTooltip());
                _iconDpi = dpi;
                return;
            }

            if (dpi != _iconDpi)
            {
                _iconDpi = dpi;
            }

            _icon.UpdateTooltip(BuildTooltip());
        }
        catch (Exception ex)
        {
            _log.Error("Could not refresh the tray icon.", ex);
        }
    }

    private static uint CurrentTrayDpi()
    {
        // The tray lives on whichever monitor the taskbar is on.
        TaskbarInfo bar = ScreenPlacement.GetTaskbar();

        MonitorGeometry monitor = bar.Bounds.Width > 0
            ? ScreenPlacement.GetMonitorFromRect(bar.Bounds)
            : ScreenPlacement.GetMonitorFromPoint(0, 0);

        return monitor.Dpi;
    }

    private string BuildTooltip()
    {
        string home = _homeAssistant.LocationName is { Length: > 0 } name
            ? name
            : "Home Assistant";

        return _homeAssistant.ConnectionState switch
        {
            HaConnectionState.Connected => $"Fate Takes You Home\n{home} · connected",
            HaConnectionState.Connecting => "Fate Takes You Home\nConnecting…",
            HaConnectionState.Authenticating => "Fate Takes You Home\nAuthenticating…",
            HaConnectionState.Reconnecting => "Fate Takes You Home\nReconnecting…",
            HaConnectionState.Failed => "Fate Takes You Home\nNot connected — click for details",
            _ => "Fate Takes You Home\nNot connected",
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _homeAssistant.PropertyChanged -= OnHomeAssistantPropertyChanged;
        _themes.Applied -= OnThemeApplied;

        if (_menu is not null)
        {
            _menu.IsOpen = false;
            _menu = null;
        }

        _flyout?.Close();
        _flyout = null;

        _mainWindow?.ForceClose();
        _mainWindow = null;

        if (_icon is not null)
        {
            _icon.Selected -= OnSelected;
            _icon.DoubleClicked -= OnDoubleClicked;
            _icon.ContextMenuRequested -= OnContextMenuRequested;
            _icon.MiddleClicked -= OnMiddleClicked;
            _icon.Dispose();
            _icon = null;
        }
    }
}
