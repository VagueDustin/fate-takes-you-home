using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FateTakesYouHome.HomeAssistant.Models;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;

namespace FateTakesYouHome.ViewModels;

/// <summary>
/// A group heading in the flat browser list.
/// </summary>
/// <remarks>
/// The list is deliberately flat — headers and entities interleaved in one collection — rather
/// than a nested items control per group. WPF cannot virtualise nested items controls, and with a
/// thousand-entity install that meant building every row and every visual for all of them up
/// front. A flat collection lets one <c>VirtualizingStackPanel</c> realise only what is on screen.
/// </remarks>
public sealed record EntityGroupHeader(string Name, int Count);

/// <summary>One row in the browser: a tile plus the controls for pinning it.</summary>
public sealed partial class BrowsableEntityViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    [ObservableProperty]
    private bool _isPinned;

    public BrowsableEntityViewModel(
        EntityTileViewModel tile, SettingsService settings, bool isPinned)
    {
        Tile = tile;
        _settings = settings;
        _isPinned = isPinned;
    }

    public EntityTileViewModel Tile { get; }

    public string EntityId => Tile.EntityId;

    /// <summary>Raised when the pin state changed, so the owner can persist it.</summary>
    public event EventHandler? PinToggled;

    [RelayCommand]
    private void TogglePin()
    {
        IsPinned = !IsPinned;
        PinToggled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets the pin state without announcing it.
    /// </summary>
    /// <remarks>
    /// Used when a cached row is reused during a rebuild. Going through the command would persist
    /// the value straight back to settings and re-enter the rebuild.
    /// </remarks>
    internal void SetPinnedQuietly(bool pinned) => IsPinned = pinned;
}

/// <summary>
/// The full entity browser: search, grouping, and pinning.
/// </summary>
/// <remarks>
/// <para>
/// Population is deferred until the page is first opened. A large Home Assistant install has
/// thousands of entities, and building view models for all of them during application startup
/// would delay the tray icon appearing — which is the one thing that has to be instant.
/// </para>
/// <para>
/// Search is debounced. Rebuilding several thousand rows on every keystroke is exactly the kind of
/// thing that makes a native app feel worse than a web page.
/// </para>
/// </remarks>
public sealed partial class EntityBrowserViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(180);

    private readonly SettingsService _settings;
    private readonly HomeAssistantService _homeAssistant;
    private readonly DispatcherTimer _searchTimer;

    private readonly Dictionary<string, EntityTileViewModel> _tiles =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, BrowsableEntityViewModel> _rows =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _loaded;
    private bool _disposed;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private EntityGrouping _grouping;

    [ObservableProperty]
    private bool _showUnavailable;

    [ObservableProperty]
    private bool _showAuxiliary;

    [ObservableProperty]
    private int _matchCount;

    [ObservableProperty]
    private int _totalCount;

    public EntityBrowserViewModel(SettingsService settings, HomeAssistantService homeAssistant)
    {
        _settings = settings;
        _homeAssistant = homeAssistant;

        _grouping = settings.Current.Grouping;
        _showUnavailable = settings.Current.ShowUnavailable;
        _showAuxiliary = settings.Current.ShowAuxiliaryEntities;

        _searchTimer = new DispatcherTimer { Interval = SearchDebounce };
        _searchTimer.Tick += OnSearchSettled;

        _homeAssistant.EntityChanged += OnEntityChanged;
        _homeAssistant.SnapshotReloaded += OnSnapshotReloaded;
    }

    /// <summary>
    /// Headers and entity rows interleaved, for a single virtualising list.
    /// </summary>
    /// <remarks>
    /// Typed as <see cref="object"/> so the two implicit <c>DataTemplate</c>s keyed by data type
    /// pick themselves. A template selector would work too but adds a class for nothing.
    /// </remarks>
    public ObservableCollection<object> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Explains an empty result without making the user guess which filter did it.</summary>
    public string EmptyMessage
    {
        get
        {
            if (!_homeAssistant.IsReady)
            {
                return "Waiting for Home Assistant to send its entity list.";
            }

            if (TotalCount == 0)
            {
                return "Home Assistant has not reported any entities this app can control.";
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                return $"Nothing matches “{SearchText}”. "
                     + (ShowUnavailable ? string.Empty : "Unavailable entities are hidden.");
            }

            return "Everything is filtered out. Try showing unavailable entities.";
        }
    }

    /// <summary>Builds the list the first time the page is shown.</summary>
    public void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        Rebuild();
    }

    // ------------------------------------------------------------------ rebuild

    private void Rebuild()
    {
        if (!_loaded)
        {
            return;
        }

        var pinned = _settings.Current.Pinned
            .Select(p => p.EntityId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<HaEntityState> candidates = _homeAssistant
            .Browsable(ShowAuxiliary, ShowUnavailable)
            .ToList();

        TotalCount = candidates.Count;

        string query = SearchText.Trim();
        if (query.Length > 0)
        {
            candidates = candidates.Where(s => Matches(s, query)).ToList();
        }

        MatchCount = candidates.Count;

        // Group, then sort inside each group. Culture-aware on the display name so the ordering
        // matches what the user is reading rather than the entity id.
        var groups = candidates
            .GroupBy(GroupNameFor)
            .OrderBy(g => g.Key == UngroupedName)
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

        Rows.Clear();

        foreach (IGrouping<string, HaEntityState> group in groups)
        {
            HaEntityState[] members = group
                .OrderBy(s => s.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            Rows.Add(new EntityGroupHeader(group.Key, members.Length));

            foreach (HaEntityState state in members)
            {
                Rows.Add(BuildRow(state, pinned.Contains(state.EntityId)));
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>
    /// Returns the row for an entity, reusing the existing one where possible.
    /// </summary>
    /// <remarks>
    /// Rows are cached alongside their tiles. Building fresh ones on every filter change would
    /// allocate a thousand view models per keystroke and leak an event subscription each time.
    /// </remarks>
    private BrowsableEntityViewModel BuildRow(HaEntityState state, bool isPinned)
    {
        if (!_tiles.TryGetValue(state.EntityId, out EntityTileViewModel? tile))
        {
            tile = new EntityTileViewModel(state, _homeAssistant);
            _tiles[state.EntityId] = tile;
        }
        else
        {
            tile.Update(state);
        }

        if (_rows.TryGetValue(state.EntityId, out BrowsableEntityViewModel? existing))
        {
            existing.SetPinnedQuietly(isPinned);
            return existing;
        }

        var row = new BrowsableEntityViewModel(tile, _settings, isPinned);
        row.PinToggled += OnPinToggled;
        _rows[state.EntityId] = row;
        return row;
    }

    private string GroupNameFor(HaEntityState state) => Grouping switch
    {
        EntityGrouping.Area => _homeAssistant.AreaFor(state.EntityId)?.Name ?? UngroupedName,
        EntityGrouping.Floor => _homeAssistant.FloorFor(state.EntityId)?.Name ?? UngroupedName,
        EntityGrouping.Domain => HaEntityState.HumaniseObjectId(state.Domain),
        _ => AllName,
    };

    private const string UngroupedName = "Unassigned";

    private const string AllName = "All entities";

    /// <summary>
    /// Matches a query against the name, the entity id and the area.
    /// </summary>
    /// <remarks>
    /// Searching the area too is what makes "kitchen" find the ceiling light that was never named
    /// after the room it is in.
    /// </remarks>
    private bool Matches(HaEntityState state, string query)
    {
        if (state.FriendlyName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        if (state.EntityId.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        HaArea? area = _homeAssistant.AreaFor(state.EntityId);
        return area is not null
               && area.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    // ------------------------------------------------------------------ pinning

    private void OnPinToggled(object? sender, EventArgs e)
    {
        if (sender is not BrowsableEntityViewModel row)
        {
            return;
        }

        List<PinnedEntity> pins = _settings.Current.Pinned;

        if (row.IsPinned)
        {
            if (!pins.Any(p => string.Equals(p.EntityId, row.EntityId, StringComparison.OrdinalIgnoreCase)))
            {
                pins.Add(new PinnedEntity { EntityId = row.EntityId });
            }
        }
        else
        {
            pins.RemoveAll(p => string.Equals(p.EntityId, row.EntityId, StringComparison.OrdinalIgnoreCase));
        }

        _settings.Save();

        // Replace rather than mutate: the flyout and dashboard rebuild from the Changed event, and
        // raising it is the only way they learn about a pin added here.
        _settings.Replace(_settings.Current);
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    // ------------------------------------------------------------------ reactions

    partial void OnSearchTextChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnSearchSettled(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        Rebuild();
    }

    partial void OnGroupingChanged(EntityGrouping value)
    {
        _settings.Current.Grouping = value;
        _settings.Save();
        Rebuild();
    }

    partial void OnShowUnavailableChanged(bool value)
    {
        _settings.Current.ShowUnavailable = value;
        _settings.Save();
        Rebuild();
    }

    partial void OnShowAuxiliaryChanged(bool value)
    {
        _settings.Current.ShowAuxiliaryEntities = value;
        _settings.Save();
        Rebuild();
    }

    private void OnEntityChanged(object? sender, EntityChangedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        // A state change only needs the tile refreshed. Regrouping on every event would rebuild
        // thousands of rows every time a power sensor ticks.
        if (e.State is not null && _tiles.TryGetValue(e.EntityId, out EntityTileViewModel? tile))
        {
            tile.Update(e.State);
            return;
        }

        // An entity appearing or disappearing changes the shape of the list, but so does every
        // sensor this browser is filtering out. Coalesce, or a busy server rebuilds continuously.
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnSnapshotReloaded(object? sender, EventArgs e)
    {
        DetachRows();
        _tiles.Clear();
        Rebuild();
    }

    private void DetachRows()
    {
        foreach (BrowsableEntityViewModel row in _rows.Values)
        {
            row.PinToggled -= OnPinToggled;
        }

        _rows.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _searchTimer.Stop();
        _searchTimer.Tick -= OnSearchSettled;

        _homeAssistant.EntityChanged -= OnEntityChanged;
        _homeAssistant.SnapshotReloaded -= OnSnapshotReloaded;

        DetachRows();

        foreach (EntityTileViewModel tile in _tiles.Values)
        {
            tile.Detach();
        }
    }
}
