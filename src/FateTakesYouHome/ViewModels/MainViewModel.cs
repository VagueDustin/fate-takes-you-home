using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.Services;

namespace FateTakesYouHome.ViewModels;

/// <summary>
/// One entry in the navigation rail.
/// </summary>
/// <remarks>
/// Selection is the navigation. Binding the radio button's <c>IsChecked</c> two-way to this and
/// acting on the change means a keyboard arrow, a screen reader, or an automation client all
/// navigate — whereas hanging navigation off the click command would only work for a mouse, and
/// would let the highlight drift away from the page actually being shown.
/// </remarks>
public sealed partial class NavigationItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public required MainWindowSection Section { get; init; }

    public required string Label { get; init; }

    /// <summary>Resource key of the icon geometry, resolved at bind time so themes can change it.</summary>
    public required string IconKey { get; init; }

    /// <summary>One line describing the page, shown as a tooltip and read by screen readers.</summary>
    public required string Description { get; init; }

    /// <summary>Resolved icon. Looked up once; the icon set does not change at runtime.</summary>
    public Geometry? Icon =>
        System.Windows.Application.Current?.TryFindResource(IconKey) as Geometry;
}

/// <summary>
/// The full window: navigation, connection banner, and the shared footer.
/// </summary>
/// <remarks>
/// Page view models are created eagerly and kept alive. There are six of them, they are cheap, and
/// keeping them means scroll position and half-finished edits survive a trip to another page —
/// which matters most on the settings page, where losing a half-typed URL would be maddening.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly HomeAssistantService _homeAssistant;
    private bool _disposed;
    private bool _syncingSelection;

    [ObservableProperty]
    private MainWindowSection _currentSection = MainWindowSection.Dashboard;

    [ObservableProperty]
    private bool _isTourRunning;

    public MainViewModel(
        AppLog log,
        SettingsService settings,
        ThemeService themes,
        HomeAssistantService homeAssistant,
        TrayController tray)
    {
        _settings = settings;
        _homeAssistant = homeAssistant;

        Dashboard = new DashboardViewModel(settings, homeAssistant, tray);
        Entities = new EntityBrowserViewModel(settings, homeAssistant);
        Themes = new ThemesViewModel(log, settings, themes);
        Settings = new SettingsPageViewModel(log, settings, homeAssistant, tray);
        Help = new HelpViewModel(settings, tray);

        _homeAssistant.PropertyChanged += OnServicePropertyChanged;
        _settings.SaveStateChanged += OnSaveStateChanged;

        NavigationItems =
        [
            new NavigationItem
            {
                Section = MainWindowSection.Dashboard,
                Label = "Home",
                IconKey = "Fate.Icon.Grid",
                Description = "The things you pinned, and what is on right now.",
            },
            new NavigationItem
            {
                Section = MainWindowSection.Entities,
                Label = "Everything",
                IconKey = "Fate.Icon.Search",
                Description = "Browse every entity Home Assistant knows about.",
            },
            new NavigationItem
            {
                Section = MainWindowSection.Themes,
                Label = "Themes",
                IconKey = "Fate.Icon.Palette",
                Description = "Change how the interface looks, or build your own theme.",
            },
            new NavigationItem
            {
                Section = MainWindowSection.Settings,
                Label = "Settings",
                IconKey = "Fate.Icon.Settings",
                Description = "Connection, startup and tray behaviour.",
            },
            new NavigationItem
            {
                Section = MainWindowSection.Help,
                Label = "Help",
                IconKey = "Fate.Icon.Book",
                Description = "Quickstart, the guided tour, and troubleshooting.",
            },
        ];

        foreach (NavigationItem item in NavigationItems)
        {
            item.PropertyChanged += OnNavigationItemChanged;
        }

        UpdateSelection();
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public DashboardViewModel Dashboard { get; }

    public EntityBrowserViewModel Entities { get; }

    public ThemesViewModel Themes { get; }

    public SettingsPageViewModel Settings { get; }

    public HelpViewModel Help { get; }

    // ------------------------------------------------------------------ connection banner

    /// <summary>True when something is wrong that the user should be told about.</summary>
    public bool ShowsConnectionBanner =>
        _homeAssistant.ConnectionState is HaConnectionState.Failed or HaConnectionState.Reconnecting
        || (!_settings.Current.IsConfigured && CurrentSection != MainWindowSection.Welcome);

    public string ConnectionBannerText =>
        !_settings.Current.IsConfigured
            ? "Not connected to Home Assistant yet. Add your server address and access token in Settings."
            : _homeAssistant.StatusMessage;

    /// <summary>Whether the banner is a hard failure or a transient one.</summary>
    public bool IsConnectionBannerSevere =>
        _homeAssistant.ConnectionState == HaConnectionState.Failed || !_settings.Current.IsConfigured;

    public bool IsConnected => _homeAssistant.ConnectionState == HaConnectionState.Connected;

    // ------------------------------------------------------------------ save-failure banner

    /// <summary>
    /// True while settings writes are failing. Shown as its own banner because the failure mode
    /// is invisible otherwise: everything appears to work, and every change is lost on exit.
    /// </summary>
    public bool ShowsSaveBanner => _settings.LastSaveError is not null;

    public string SaveBannerText =>
        _settings.LastSaveError is { } error
            ? $"Changes cannot be saved right now — {error} Pins and settings will be lost when the app closes."
            : string.Empty;

    [RelayCommand]
    private void RetrySave() => _settings.SaveNow();

    private void OnSaveStateChanged(object? sender, EventArgs e)
    {
        // Raised from the save timer's worker thread; the banner binds on the UI thread.
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(ShowsSaveBanner));
            OnPropertyChanged(nameof(SaveBannerText));
        });
    }

    public string ConnectionSummary => _homeAssistant.ConnectionState switch
    {
        HaConnectionState.Connected =>
            _homeAssistant.LocationName is { Length: > 0 } name
                ? $"{name} · Home Assistant {_homeAssistant.ServerVersion}"
                : $"Connected · Home Assistant {_homeAssistant.ServerVersion}",
        HaConnectionState.Connecting => "Connecting…",
        HaConnectionState.Authenticating => "Authenticating…",
        HaConnectionState.Reconnecting => "Reconnecting…",
        HaConnectionState.Failed => "Not connected",
        _ => "Disconnected",
    };

    /// <summary>The publisher credit the brand requires on every property.</summary>
    public static string FooterLine =>
        $"Provided by VagueDustin Enterprises™ · © {DateTime.Now.Year} Fate Takes You Home. "
        + "All rights reserved.";

    public static string VersionLine => $"Version {App.DisplayVersion}";

    // ------------------------------------------------------------------ navigation

    [RelayCommand]
    public void Navigate(MainWindowSection section)
    {
        CurrentSection = section;
    }

    [RelayCommand]
    private async Task ReconnectAsync() => await _homeAssistant.ReconnectAsync().ConfigureAwait(true);

    [RelayCommand]
    private void GoToSettings() => Navigate(MainWindowSection.Settings);

    partial void OnCurrentSectionChanged(MainWindowSection value)
    {
        UpdateSelection();

        OnPropertyChanged(nameof(ShowsConnectionBanner));

        // The browser is expensive to populate; only do it when it is actually being looked at.
        if (value == MainWindowSection.Entities)
        {
            Entities.EnsureLoaded();
        }
    }

    private void UpdateSelection()
    {
        _syncingSelection = true;

        try
        {
            foreach (NavigationItem item in NavigationItems)
            {
                item.IsSelected = item.Section == CurrentSection;
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>Navigates when a rail item becomes selected by any means.</summary>
    private void OnNavigationItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Ignore the echo from UpdateSelection writing the flags back.
        if (_syncingSelection || e.PropertyName != nameof(NavigationItem.IsSelected))
        {
            return;
        }

        if (sender is NavigationItem { IsSelected: true } item && item.Section != CurrentSection)
        {
            CurrentSection = item.Section;
        }
    }

    private void OnServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HomeAssistantService.ConnectionState):
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(ConnectionSummary));
                OnPropertyChanged(nameof(ShowsConnectionBanner));
                OnPropertyChanged(nameof(IsConnectionBannerSevere));
                break;

            case nameof(HomeAssistantService.StatusMessage):
                OnPropertyChanged(nameof(ConnectionBannerText));
                break;

            case nameof(HomeAssistantService.LocationName):
            case nameof(HomeAssistantService.ServerVersion):
                OnPropertyChanged(nameof(ConnectionSummary));
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _homeAssistant.PropertyChanged -= OnServicePropertyChanged;
        _settings.SaveStateChanged -= OnSaveStateChanged;

        foreach (NavigationItem item in NavigationItems)
        {
            item.PropertyChanged -= OnNavigationItemChanged;
        }

        Dashboard.Dispose();
        Entities.Dispose();
        Themes.Dispose();
        Settings.Dispose();
    }
}
