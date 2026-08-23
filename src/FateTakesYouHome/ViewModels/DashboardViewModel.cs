using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.HomeAssistant.Models;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;

namespace FateTakesYouHome.ViewModels;

/// <summary>A count of things currently on, for one domain.</summary>
public sealed record ActivitySummary(string Label, int Count, string IconKey);

/// <summary>
/// The landing page: the pins, and a short account of what is currently on.
/// </summary>
/// <remarks>
/// The activity summary exists because the first question anybody asks a home dashboard is "did I
/// leave something on". Answering it in one line at the top is more useful than another grid.
/// </remarks>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly HomeAssistantService _homeAssistant;
    private readonly TrayController _tray;

    private readonly Dictionary<string, EntityTileViewModel> _byEntityId =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public DashboardViewModel(
        SettingsService settings, HomeAssistantService homeAssistant, TrayController tray)
    {
        _settings = settings;
        _homeAssistant = homeAssistant;
        _tray = tray;

        _homeAssistant.EntityChanged += OnEntityChanged;
        _homeAssistant.SnapshotReloaded += OnSnapshotReloaded;
        _settings.Changed += OnSettingsChanged;

        Rebuild();
    }

    public ObservableCollection<EntityTileViewModel> Pinned { get; } = [];

    public ObservableCollection<ActivitySummary> Activity { get; } = [];

    public bool HasPins => Pinned.Count > 0;

    public bool HasActivity => Activity.Count > 0;

    /// <summary>The line under the page title. Mythic register is allowed here; it is a heading.</summary>
    public string Greeting
    {
        get
        {
            if (!_homeAssistant.IsReady)
            {
                return "Waiting on Home Assistant.";
            }

            int lightsOn = CountOn(HaDomains.Light);

            return lightsOn switch
            {
                0 => "Everything is dark. The house is asleep.",
                1 => "One light still burning.",
                _ => $"{lightsOn} lights still burning.",
            };
        }
    }

    [RelayCommand]
    private void BrowseEntities() => BrowseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task TurnOffAllLightsAsync()
    {
        CommandResult result = await _homeAssistant
            .ExecuteAsync(
                (client, ct) => client.CallServiceAsync(
                    HaDomains.Light,
                    "turn_off",
                    new Dictionary<string, object?> { ["entity_id"] = "all" },
                    data: null,
                    ct),
                "Turn off all lights")
            .ConfigureAwait(true);

        LastActionMessage = result.Succeeded
            ? "All lights off."
            : result.ErrorMessage;
    }

    [ObservableProperty]
    private string? _lastActionMessage;

    /// <summary>Raised when the user asks to go and pin something.</summary>
    public event EventHandler? BrowseRequested;

    /// <summary>Rebuilds the pin tiles and the activity counts.</summary>
    public void Rebuild()
    {
        foreach (EntityTileViewModel tile in Pinned)
        {
            tile.Detach();
        }

        Pinned.Clear();
        _byEntityId.Clear();

        foreach (PinnedEntity pin in _settings.Current.Pinned)
        {
            HaEntityState? state = _homeAssistant.Find(pin.EntityId);

            if (state is null)
            {
                continue;
            }

            var tile = new EntityTileViewModel(state, _homeAssistant, pin.Label);
            Pinned.Add(tile);
            _byEntityId[pin.EntityId] = tile;
        }

        RebuildActivity();

        OnPropertyChanged(nameof(HasPins));
        OnPropertyChanged(nameof(Greeting));
    }

    private void RebuildActivity()
    {
        Activity.Clear();

        AddIfAny("Lights on", CountOn(HaDomains.Light), "Fate.Icon.Power");
        AddIfAny("Switches on", CountOn(HaDomains.Switch), "Fate.Icon.Power");
        AddIfAny("Fans running", CountOn(HaDomains.Fan), "Fate.Icon.Refresh");
        AddIfAny("Covers open", CountOn(HaDomains.Cover), "Fate.Icon.ChevronUp");
        AddIfAny("Media playing", CountPlaying(), "Fate.Icon.Power");
        AddIfAny("Doors unlocked", CountOn(HaDomains.Lock), "Fate.Icon.Link");

        OnPropertyChanged(nameof(HasActivity));

        void AddIfAny(string label, int count, string iconKey)
        {
            if (count > 0)
            {
                Activity.Add(new ActivitySummary(label, count, iconKey));
            }
        }
    }

    private int CountOn(string domain) =>
        _homeAssistant.States.Count(s => s.Domain == domain && !s.IsUnavailable && s.IsOn);

    private int CountPlaying() =>
        _homeAssistant.States.Count(
            s => s.Domain == HaDomains.MediaPlayer && s.State == "playing");

    private void OnEntityChanged(object? sender, EntityChangedEventArgs e)
    {
        if (_byEntityId.TryGetValue(e.EntityId, out EntityTileViewModel? tile) && e.State is not null)
        {
            tile.Update(e.State);
        }
        else if (_settings.Current.Pinned.Any(
                     p => string.Equals(p.EntityId, e.EntityId, StringComparison.OrdinalIgnoreCase)))
        {
            Rebuild();
            return;
        }

        // The counts are cheap to recompute and wrong the moment anything changes.
        RebuildActivity();
        OnPropertyChanged(nameof(Greeting));
    }

    private void OnSnapshotReloaded(object? sender, EventArgs e) => Rebuild();

    private void OnSettingsChanged(object? sender, EventArgs e) => Rebuild();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _homeAssistant.EntityChanged -= OnEntityChanged;
        _homeAssistant.SnapshotReloaded -= OnSnapshotReloaded;
        _settings.Changed -= OnSettingsChanged;

        foreach (EntityTileViewModel tile in Pinned)
        {
            tile.Detach();
        }
    }
}
