using System.Collections.ObjectModel;
using System.Windows.Threading;
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

    /// <summary>
    /// How long to wait for state churn to settle before recounting.
    /// </summary>
    /// <remarks>
    /// A busy Home Assistant emits events continuously. Nobody needs a whole-house count accurate
    /// to the millisecond, and recomputing per event is what made the interface stutter.
    /// </remarks>
    private static readonly TimeSpan ActivityDebounce = TimeSpan.FromMilliseconds(400);

    private readonly DispatcherTimer _activityDebounce;

    private int _lightsOn;
    private int _lastLights = -1;
    private int _lastSwitches = -1;
    private int _lastFans = -1;
    private int _lastCovers = -1;
    private int _lastMedia = -1;
    private int _lastLocks = -1;
    private bool _disposed;

    public DashboardViewModel(
        SettingsService settings, HomeAssistantService homeAssistant, TrayController tray)
    {
        _settings = settings;
        _homeAssistant = homeAssistant;
        _tray = tray;

        _activityDebounce = new DispatcherTimer { Interval = ActivityDebounce };
        _activityDebounce.Tick += OnActivitySettled;

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

            // Reads the count cached by RebuildActivity rather than rescanning; this getter is hit
            // by every binding refresh.
            return _lightsOn switch
            {
                0 => "Everything is dark. The house is asleep.",
                1 => "One light still burning.",
                _ => $"{_lightsOn} lights still burning.",
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

    /// <summary>
    /// Recomputes the activity counts in a single pass.
    /// </summary>
    /// <remarks>
    /// This used to run six separate LINQ scans, each over a freshly allocated snapshot of every
    /// entity, on every <c>state_changed</c> event. On a thousand-entity install with sensors
    /// reporting continuously that is thousands of array allocations a second, and it was the
    /// largest single cause of the interface stuttering. Now: one pass, no allocation, debounced,
    /// and the collection is only rebuilt when a count actually moved.
    /// </remarks>
    private void RebuildActivity()
    {
        int lights = 0, switches = 0, fans = 0, covers = 0, media = 0, locks = 0;

        foreach (HaEntityState state in _homeAssistant.EnumerateStates())
        {
            switch (state.Domain)
            {
                case HaDomains.Light when !state.IsUnavailable && state.IsOn:
                    lights++;
                    break;

                case HaDomains.Switch when !state.IsUnavailable && state.IsOn:
                    switches++;
                    break;

                case HaDomains.Fan when !state.IsUnavailable && state.IsOn:
                    fans++;
                    break;

                case HaDomains.Cover when !state.IsUnavailable && state.IsOn:
                    covers++;
                    break;

                case HaDomains.Lock when !state.IsUnavailable && state.IsOn:
                    locks++;
                    break;

                case HaDomains.MediaPlayer when state.State == "playing":
                    media++;
                    break;
            }
        }

        _lightsOn = lights;

        if (lights == _lastLights && switches == _lastSwitches && fans == _lastFans
            && covers == _lastCovers && media == _lastMedia && locks == _lastLocks)
        {
            return;
        }

        (_lastLights, _lastSwitches, _lastFans, _lastCovers, _lastMedia, _lastLocks) =
            (lights, switches, fans, covers, media, locks);

        Activity.Clear();

        AddIfAny("Lights on", lights, "Fate.Icon.Power");
        AddIfAny("Switches on", switches, "Fate.Icon.Power");
        AddIfAny("Fans running", fans, "Fate.Icon.Refresh");
        AddIfAny("Covers open", covers, "Fate.Icon.ChevronUp");
        AddIfAny("Media playing", media, "Fate.Icon.Power");
        AddIfAny("Doors unlocked", locks, "Fate.Icon.Link");

        OnPropertyChanged(nameof(HasActivity));
        OnPropertyChanged(nameof(Greeting));

        void AddIfAny(string label, int count, string iconKey)
        {
            if (count > 0)
            {
                Activity.Add(new ActivitySummary(label, count, iconKey));
            }
        }
    }

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

        // The counts are whole-house aggregates, so they are coalesced. Recomputing per event
        // means recomputing continuously, because the events do not stop.
        _activityDebounce.Stop();
        _activityDebounce.Start();
    }

    private void OnActivitySettled(object? sender, EventArgs e)
    {
        _activityDebounce.Stop();
        RebuildActivity();
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

        _activityDebounce.Stop();
        _activityDebounce.Tick -= OnActivitySettled;

        _homeAssistant.EntityChanged -= OnEntityChanged;
        _homeAssistant.SnapshotReloaded -= OnSnapshotReloaded;
        _settings.Changed -= OnSettingsChanged;

        foreach (EntityTileViewModel tile in Pinned)
        {
            tile.Detach();
        }
    }
}
