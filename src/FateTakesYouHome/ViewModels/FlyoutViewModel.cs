using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.HomeAssistant.Models;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;

namespace FateTakesYouHome.ViewModels;

/// <summary>
/// The tray panel: the handful of things somebody actually reaches for.
/// </summary>
/// <remarks>
/// Shows only pinned entities, in the order the user pinned them. Auto-populating it from the
/// whole house would make it a worse version of the entity browser; the value of a tray panel is
/// that it is short.
/// </remarks>
public sealed partial class FlyoutViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly HomeAssistantService _homeAssistant;
    private readonly Dictionary<string, EntityTileViewModel> _byEntityId =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public FlyoutViewModel(SettingsService settings, HomeAssistantService homeAssistant)
    {
        _settings = settings;
        _homeAssistant = homeAssistant;

        _homeAssistant.PropertyChanged += OnServicePropertyChanged;
        _homeAssistant.EntityChanged += OnEntityChanged;
        _homeAssistant.SnapshotReloaded += OnSnapshotReloaded;
        _settings.Changed += OnSettingsChanged;

        Rebuild();
    }

    /// <summary>The pinned tiles, in the user's own order.</summary>
    public ObservableCollection<EntityTileViewModel> Tiles { get; } = [];

    /// <summary>The customised layout, when the user has arranged one.</summary>
    public ObservableCollection<WidgetViewModel> Widgets { get; } = [];

    /// <summary>True when the panel renders the widget grid instead of the pinned list.</summary>
    public bool UsesCustomLayout => _settings.Current.FlyoutWidgets is { Count: > 0 };

    /// <summary>The panel grid is four columns wide; the editor and the panel must agree.</summary>
    public const int FlyoutGridColumns = 4;

    /// <summary>Where the panel should send the user when there is nothing pinned.</summary>
    public bool IsEmpty => Tiles.Count == 0;

    public bool IsConfigured => _settings.Current.IsConfigured;

    public HaConnectionState ConnectionState => _homeAssistant.ConnectionState;

    public string StatusMessage => _homeAssistant.StatusMessage;

    public bool IsConnected => _homeAssistant.ConnectionState == HaConnectionState.Connected;

    /// <summary>Shown in the header. The name of the house, when Home Assistant has told us one.</summary>
    public string HomeName =>
        _homeAssistant.LocationName is { Length: > 0 } name ? name : "Home Assistant";

    /// <summary>True while the panel has nothing useful to show and should explain itself.</summary>
    public bool ShowsPlaceholder => !IsConfigured || !IsConnected || (IsEmpty && !UsesCustomLayout);

    /// <summary>The stock footer belongs to the stock layout; a custom grid brings its own actions.</summary>
    public bool ShowsStandardFooter => !ShowsPlaceholder && !UsesCustomLayout;

    /// <summary>The headline for the placeholder state.</summary>
    public string PlaceholderTitle
    {
        get
        {
            if (!IsConfigured)
            {
                return "Not connected yet";
            }

            return ConnectionState switch
            {
                HaConnectionState.Connected => "Nothing pinned yet",
                HaConnectionState.Failed => "Could not connect",
                HaConnectionState.Reconnecting => "Reconnecting",
                _ => "Connecting",
            };
        }
    }

    /// <summary>The explanation under the headline. Functional register — no flourish here.</summary>
    public string PlaceholderDetail
    {
        get
        {
            if (!IsConfigured)
            {
                return "Add your Home Assistant address and access token to get started.";
            }

            if (ConnectionState != HaConnectionState.Connected)
            {
                return StatusMessage;
            }

            return "Pin the lights, scenes and automations you reach for most, and they will "
                 + "appear here.";
        }
    }

    /// <summary>The label on the placeholder's button.</summary>
    public string PlaceholderAction => IsConfigured && IsConnected ? "Choose what appears here" : "Open settings";

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Expand() => ExpandRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task ReconnectAsync() => await _homeAssistant.ReconnectAsync().ConfigureAwait(true);

    /// <summary>
    /// The one whole-house action worth a permanent place in the panel: leaving. Everything else
    /// is a pin.
    /// </summary>
    [RelayCommand]
    private async Task TurnOffAllLightsAsync()
    {
        CommandResult result = await _homeAssistant.TurnOffAllLightsAsync().ConfigureAwait(true);

        LastActionMessage = result.Succeeded ? "All lights off." : result.ErrorMessage;
    }

    [ObservableProperty]
    private string? _lastActionMessage;

    /// <summary>Raised when the user asks for the settings page.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the user asks for the full window.</summary>
    public event EventHandler? ExpandRequested;

    /// <summary>Rebuilds the tile list from the pinned settings.</summary>
    public void Rebuild()
    {
        foreach (EntityTileViewModel tile in Tiles)
        {
            tile.Detach();
        }

        Tiles.Clear();
        _byEntityId.Clear();

        foreach (PinnedEntity pin in _settings.Current.Pinned)
        {
            HaEntityState? state = _homeAssistant.Find(pin.EntityId);

            if (state is null)
            {
                // The entity is pinned but not present: either not loaded yet, or it was removed
                // from Home Assistant. Either way there is nothing to draw, and dropping the pin
                // would lose the user's arrangement over a transient outage.
                continue;
            }

            var tile = new EntityTileViewModel(state, _homeAssistant, pin.Label);
            Tiles.Add(tile);
            _byEntityId[pin.EntityId] = tile;
        }

        RebuildWidgets();
        RaisePlaceholderProperties();
        OnPropertyChanged(nameof(UsesCustomLayout));
    }

    private void RebuildWidgets()
    {
        foreach (WidgetViewModel widget in Widgets)
        {
            widget.Dispose();
        }

        Widgets.Clear();

        if (_settings.Current.FlyoutWidgets is not { Count: > 0 } specs)
        {
            return;
        }

        foreach (WidgetSpec spec in specs)
        {
            spec.ClampTo(FlyoutGridColumns);

            string? label = spec.EntityId is null
                ? null
                : _settings.Current.Pinned
                    .FirstOrDefault(p => string.Equals(p.EntityId, spec.EntityId, StringComparison.OrdinalIgnoreCase))?
                    .Label;

            // Rooms and activity widgets need the dashboard's tallies, which the panel does not
            // carry; the factory skips them here.
            if (WidgetFactory.Build(spec, _homeAssistant, null, null, label) is { } widget)
            {
                Widgets.Add(widget);
            }
        }
    }

    private void OnEntityChanged(object? sender, EntityChangedEventArgs e)
    {
        if (!_byEntityId.TryGetValue(e.EntityId, out EntityTileViewModel? tile))
        {
            // A pinned entity that was missing may have just appeared.
            if (_settings.Current.Pinned.Any(
                    p => string.Equals(p.EntityId, e.EntityId, StringComparison.OrdinalIgnoreCase)))
            {
                Rebuild();
            }

            return;
        }

        if (e.State is null)
        {
            Rebuild();
            return;
        }

        tile.Update(e.State);
    }

    private void OnSnapshotReloaded(object? sender, EventArgs e) => Rebuild();

    private void OnSettingsChanged(object? sender, EventArgs e) => Rebuild();

    private void OnServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HomeAssistantService.ConnectionState):
                OnPropertyChanged(nameof(ConnectionState));
                OnPropertyChanged(nameof(IsConnected));
                RaisePlaceholderProperties();
                break;

            case nameof(HomeAssistantService.StatusMessage):
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(PlaceholderDetail));
                break;

            case nameof(HomeAssistantService.LocationName):
                OnPropertyChanged(nameof(HomeName));
                break;
        }
    }

    private void RaisePlaceholderProperties()
    {
        OnPropertyChanged(nameof(ShowsStandardFooter));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(ShowsPlaceholder));
        OnPropertyChanged(nameof(PlaceholderTitle));
        OnPropertyChanged(nameof(PlaceholderDetail));
        OnPropertyChanged(nameof(PlaceholderAction));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _homeAssistant.PropertyChanged -= OnServicePropertyChanged;
        _homeAssistant.EntityChanged -= OnEntityChanged;
        _homeAssistant.SnapshotReloaded -= OnSnapshotReloaded;
        _settings.Changed -= OnSettingsChanged;

        foreach (EntityTileViewModel tile in Tiles)
        {
            tile.Detach();
        }
    }
}
