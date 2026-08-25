// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FateTakesYouHome.Branding;
using FateTakesYouHome.Onboarding;
using FateTakesYouHome.Services;
using FateTakesYouHome.Theming.Model;
using FateTakesYouHome.ViewModels;

namespace FateTakesYouHome.Views;

/// <summary>
/// The full window: navigation, pages, and the guided tour.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppLog _log;
    private readonly SettingsService _settings;
    private readonly ThemeService _themes;
    private readonly MainViewModel _viewModel;

    private bool _reallyClosing;

    public MainWindow(
        AppLog log,
        SettingsService settings,
        ThemeService themes,
        HomeAssistantService homeAssistant,
        TrayController tray,
        UpdateService updates)
    {
        _log = log;
        _settings = settings;
        _themes = themes;

        InitializeComponent();

        _viewModel = new MainViewModel(log, settings, themes, homeAssistant, tray, updates);
        DataContext = _viewModel;

        TitleMark.Source = FateMark.Render(32, withPlate: false);
        Icon = FateMark.Render(64);

        _viewModel.Dashboard.BrowseRequested +=
            (_, _) => _viewModel.Navigate(MainWindowSection.Entities);

        // A room card is a filtered view of Everything: search already knows how to match areas.
        _viewModel.Dashboard.RoomSelected += (_, room) =>
        {
            _viewModel.Entities.SearchText = room;
            _viewModel.Navigate(MainWindowSection.Entities);
        };

        _viewModel.Help.TourRequested += (_, _) => StartTour();

        WelcomePage.GetStarted += (_, _) => _viewModel.Navigate(MainWindowSection.Settings);
        WelcomePage.TourRequested += (_, _) => StartTour();
        WelcomePage.Dismissed += (_, _) => CompleteOnboarding(startedTour: false);

        Tour.Finished += OnTourFinished;

        // The mouse's own back and forward buttons, honoured the way a browser would.
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1)
            {
                _viewModel.GoBack();
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.XButton2)
            {
                _viewModel.GoForward();
                e.Handled = true;
            }
        };
        _themes.Applied += OnThemeApplied;
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
    }

    /// <summary>Switches to a page.</summary>
    public void Navigate(MainWindowSection section)
    {
        // Welcome is only reachable on first run; afterwards it would be a dead end.
        if (section == MainWindowSection.Welcome && _settings.Current.HasCompletedOnboarding)
        {
            section = MainWindowSection.Dashboard;
        }

        _viewModel.Navigate(section);

        if (section == MainWindowSection.Help)
        {
            _viewModel.Help.Refresh();
        }
    }

    // ------------------------------------------------------------------ the tour

    /// <summary>
    /// Runs the first-run walkthrough.
    /// </summary>
    /// <remarks>
    /// Each step resolves its target lazily and navigates first, so the tour can walk across pages
    /// rather than only pointing at things that happen to be on screen already.
    /// </remarks>
    public void StartTour()
    {
        _settings.Current.HasCompletedOnboarding = true;
        _settings.Save();

        Tour.Start(
        [
            new TourStep
            {
                Title = "This is the whole application",
                Body = "Fate Takes You Home lives in your notification area. This window is for "
                     + "setting it up and browsing everything; the day-to-day is one click on the "
                     + "tray icon.",
            },
            new TourStep
            {
                Title = "Connect it to Home Assistant",
                Body = "Settings is where your server address and Long-Lived Access Token go. Test "
                     + "connection tells you whether the two agree before you commit them.",
                Prepare = () => _viewModel.Navigate(MainWindowSection.Settings),
                Target = () => SettingsPage.ConnectionSection,
            },
            new TourStep
            {
                Title = "Find what you want to control",
                Body = "Everything Home Assistant knows about, grouped by room. Search matches the "
                     + "name, the entity id and the area — so “kitchen” finds the ceiling light "
                     + "even if nobody named it after the room.",
                Prepare = () => _viewModel.Navigate(MainWindowSection.Entities),
                Target = () => EntitiesPage.SearchBox,
            },
            new TourStep
            {
                Title = "Pin the ones you use",
                Body = "The pin button on each row puts it in the tray panel. Pin the handful you "
                     + "reach for daily; the panel is useful because it is short.",
                Prepare = () => _viewModel.Navigate(MainWindowSection.Entities),
                Target = () => EntitiesPage.ResultsList,
                Padding = 4,
            },
            new TourStep
            {
                Title = "Make it yours",
                Body = "FATE is the house theme, but every colour, corner and animation is a value "
                     + "you can change. Edit a theme and the interface repaints as you type.",
                Prepare = () => _viewModel.Navigate(MainWindowSection.Themes),
                Target = () => ThemesPage.ThemeList,
            },
            new TourStep
            {
                Title = "That is everything",
                Body = "Click the tray icon for the panel, double-click for this window. Help has "
                     + "the quickstart and the answers to the things that usually go wrong.",
                Prepare = () => _viewModel.Navigate(MainWindowSection.Help),
                Target = () => NavigationRail,
            },
        ]);
    }

    private void OnTourFinished(object? sender, bool completed)
    {
        CompleteOnboarding(startedTour: true);

        if (completed)
        {
            _log.Info("The guided tour was completed.");
            _viewModel.Navigate(MainWindowSection.Dashboard);
        }
    }

    private void CompleteOnboarding(bool startedTour)
    {
        if (!_settings.Current.HasCompletedOnboarding)
        {
            _settings.Current.HasCompletedOnboarding = true;
            _settings.Save();
        }

        if (!startedTour && _viewModel.CurrentSection == MainWindowSection.Welcome)
        {
            _viewModel.Navigate(MainWindowSection.Dashboard);
        }
    }

    // ------------------------------------------------------------------ chrome

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            ApplyChrome(source.Handle);
        }
    }

    private void ApplyChrome(IntPtr handle)
    {
        Theme theme = _themes.Current;

        Interop.WindowEffects.SetDarkMode(handle, theme.Appearance == ThemeAppearance.Dark);

        Interop.WindowEffects.SetCornerPreference(handle, Interop.WindowCorner.Round);

        // The main window is opaque, so unlike the flyout it can take a real system backdrop.
        if (theme.Backdrop.Mode is BackdropMode.Mica or BackdropMode.Acrylic)
        {
            if (!Interop.WindowEffects.SetSystemBackdrop(handle, theme.Backdrop.Mode))
            {
                _log.Debug("The system backdrop was refused; keeping the painted background.");
            }
        }
    }

    private void OnThemeApplied(object? sender, ThemeAppliedEventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            ApplyChrome(source.Handle);
        }
    }

    private void OnMinimiseClicked(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximiseClicked(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // A maximised window under WindowChrome overhangs the screen by the resize border unless
        // the margin is compensated, which shows up as content sliding under the taskbar.
        RootLayer.Margin = WindowState == WindowState.Maximized
            ? new Thickness(7)
            : new Thickness(0);
    }

    // ------------------------------------------------------------------ lifetime

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window is not closing the application. The tray icon is the application.
        if (!_reallyClosing && _settings.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _themes.Applied -= OnThemeApplied;
        Tour.Finished -= OnTourFinished;
        _viewModel.Dispose();

        base.OnClosing(e);
    }

    /// <summary>Closes for real, ignoring the close-to-tray preference.</summary>
    public new void Close()
    {
        if (_settings.Current.CloseToTray)
        {
            Hide();
            return;
        }

        _reallyClosing = true;
        base.Close();
    }

    /// <summary>Used at shutdown, when the window must actually go away.</summary>
    public void ForceClose()
    {
        _reallyClosing = true;
        base.Close();
    }
}
