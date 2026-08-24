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

/// <summary>One room's light situation, for the dashboard's room grid.</summary>
public sealed record RoomSummary(string Name, int LightsOn, int LightsTotal)
{
    public string Detail => LightsOn switch
    {
        0 => "Dark",
        1 => "1 light on",
        _ => $"{LightsOn} lights on",
    };

    public bool IsLit => LightsOn > 0;
}

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

    /// <summary>The customised layout, when the user has arranged one.</summary>
    public ObservableCollection<WidgetViewModel> Widgets { get; } = [];

    /// <summary>True when the home screen renders the widget grid instead of the standard view.</summary>
    public bool UsesCustomLayout => _settings.Current.HomeWidgets is { Count: > 0 };

    public ObservableCollection<ActivitySummary> Activity { get; } = [];

    /// <summary>Every room that has lights, lit rooms first.</summary>
    public ObservableCollection<RoomSummary> Rooms { get; } = [];

    public bool HasPins => Pinned.Count > 0;

    public bool HasActivity => Activity.Count > 0;

    public bool HasRooms => Rooms.Count > 0;

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
        CommandResult result = await _homeAssistant.TurnOffAllLightsAsync().ConfigureAwait(true);

        LastActionMessage = result.Succeeded
            ? "All lights off."
            : result.ErrorMessage;
    }

    [ObservableProperty]
    private string? _lastActionMessage;

    /// <summary>Raised when the user asks to go and pin something.</summary>
    public event EventHandler? BrowseRequested;

    /// <summary>Raised when the user clicks a room, carrying the room's name.</summary>
    public event EventHandler<string>? RoomSelected;

    [RelayCommand]
    private void OpenRoom(RoomSummary? room)
    {
        if (room is not null)
        {
            RoomSelected?.Invoke(this, room.Name);
        }
    }

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
        RebuildWidgets();

        OnPropertyChanged(nameof(HasPins));
        OnPropertyChanged(nameof(Greeting));
        OnPropertyChanged(nameof(UsesCustomLayout));
    }

    /// <summary>Rebuilds the widget grid from the saved layout, when there is one.</summary>
    private void RebuildWidgets()
    {
        foreach (WidgetViewModel widget in Widgets)
        {
            widget.Dispose();
        }

        Widgets.Clear();

        if (_settings.Current.HomeWidgets is not { Count: > 0 } specs)
        {
            return;
        }

        foreach (Models.WidgetSpec spec in specs)
        {
            spec.ClampTo(HomeGridColumns);

            if (WidgetFactory.Build(spec, _homeAssistant, Rooms, Activity, PinLabelFor(spec.EntityId))
                is { } widget)
            {
                Widgets.Add(widget);
            }
        }
    }

    /// <summary>The home grid is six columns wide; the editor and the page must agree.</summary>
    public const int HomeGridColumns = 6;

    private string? PinLabelFor(string? entityId) =>
        entityId is null
            ? null
            : _settings.Current.Pinned
                .FirstOrDefault(p => string.Equals(p.EntityId, entityId, StringComparison.OrdinalIgnoreCase))?
                .Label;

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
        Dictionary<string, (int On, int Total)>? byRoom = null;

        foreach (HaEntityState state in _homeAssistant.EnumerateStates())
        {
            // Rooms are tallied in the same pass: every light, lit or not, is attributed to its
            // area so a room can honestly say "dark" rather than disappearing.
            if (state.Domain == HaDomains.Light && !state.IsUnavailable
                && _homeAssistant.AreaFor(state.EntityId) is { } area
                && !string.IsNullOrWhiteSpace(area.Name))
            {
                byRoom ??= new Dictionary<string, (int, int)>(StringComparer.CurrentCultureIgnoreCase);
                (int on, int total) = byRoom.TryGetValue(area.Name, out (int On, int Total) t)
                    ? (t.On, t.Total)
                    : (0, 0);
                byRoom[area.Name] = (on + (state.IsOn ? 1 : 0), total + 1);
            }

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

        RebuildRooms(byRoom);

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

    /// <summary>Publishes the room tallies, touching the collection only when they moved.</summary>
    private void RebuildRooms(Dictionary<string, (int On, int Total)>? byRoom)
    {
        List<RoomSummary> next = byRoom is null
            ? []
            : byRoom
                .Select(pair => new RoomSummary(pair.Key, pair.Value.On, pair.Value.Total))
                .OrderByDescending(room => room.IsLit)
                .ThenBy(room => room.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        // Records compare by value, so this is a cheap "did anything actually change".
        if (next.Count == Rooms.Count && next.SequenceEqual(Rooms))
        {
            return;
        }

        Rooms.Clear();
        foreach (RoomSummary room in next)
        {
            Rooms.Add(room);
        }

        OnPropertyChanged(nameof(HasRooms));
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

        foreach (WidgetViewModel widget in Widgets)
        {
            widget.Dispose();
        }
    }
}
