using System.Collections.Concurrent;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.HomeAssistant.Models;

namespace FateTakesYouHome.Services;

/// <summary>Raised when one entity's state changed.</summary>
public sealed class EntityChangedEventArgs(string entityId, HaEntityState? state) : EventArgs
{
    public string EntityId { get; } = entityId;

    /// <summary>The new state, or null when the entity was removed.</summary>
    public HaEntityState? State { get; } = state;
}

/// <summary>The outcome of a service call the user triggered.</summary>
public sealed record CommandResult(bool Succeeded, string? ErrorMessage)
{
    public static CommandResult Ok { get; } = new(true, null);

    public static CommandResult Failed(string message) => new(false, message);
}

/// <summary>
/// The application's view of Home Assistant: one connection, one entity cache, one place errors
/// are turned into something worth showing a person.
/// </summary>
/// <remarks>
/// <para>
/// Every event from <see cref="HaClient"/> arrives on a background thread. This class is the
/// boundary where that becomes UI-thread state, so no view model ever has to think about it.
/// </para>
/// <para>
/// The cache is rebuilt in full after each reconnect rather than patched. State changes during an
/// outage were never delivered, so anything short of a full re-read leaves stale tiles that lie
/// about the state of somebody's house.
/// </para>
/// </remarks>
public sealed partial class HomeAssistantService : ObservableObject, IAsyncDisposable
{
    private readonly AppLog _log;
    private readonly Dispatcher _dispatcher;

    private readonly ConcurrentDictionary<string, HaEntityState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, HaArea> _areasById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HaFloor> _floorsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HaDevice> _devicesById = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, HaEntityRegistryEntry> _registryByEntity =
        new(StringComparer.OrdinalIgnoreCase);

    private HaClient? _client;
    private CancellationTokenSource? _snapshotCancellation;
    private bool _disposed;

    [ObservableProperty]
    private HaConnectionState _connectionState = HaConnectionState.Disconnected;

    [ObservableProperty]
    private string _statusMessage = "Not configured yet.";

    [ObservableProperty]
    private string? _serverVersion;

    [ObservableProperty]
    private string? _locationName;

    [ObservableProperty]
    private bool _isLoadingSnapshot;

    public HomeAssistantService(AppLog log, Dispatcher dispatcher)
    {
        _log = log;
        _dispatcher = dispatcher;
    }

    /// <summary>Raised on the UI thread for each entity state change.</summary>
    public event EventHandler<EntityChangedEventArgs>? EntityChanged;

    /// <summary>Raised on the UI thread after the full entity set has been reloaded.</summary>
    public event EventHandler? SnapshotReloaded;

    /// <summary>True when connected and the first snapshot has arrived.</summary>
    public bool IsReady => ConnectionState == HaConnectionState.Connected && !IsLoadingSnapshot;

    /// <summary>
    /// A snapshot of every known entity state.
    /// </summary>
    /// <remarks>
    /// Allocates. With a thousand-entity install this is far too expensive to call from an event
    /// handler — use <see cref="EnumerateStates"/> or <see cref="CountStates"/> on any hot path.
    /// </remarks>
    public IReadOnlyCollection<HaEntityState> States => _states.Values.ToArray();

    /// <summary>How many entities are known. Free; no enumeration.</summary>
    public int StateCount => _states.Count;

    /// <summary>
    /// Walks the entity states without allocating a snapshot.
    /// </summary>
    /// <remarks>
    /// <see cref="ConcurrentDictionary{TKey,TValue}.Values"/> builds a whole new collection every
    /// time it is read, so it is not usable from a per-event code path. Enumerating the dictionary
    /// itself does not.
    /// </remarks>
    public IEnumerable<HaEntityState> EnumerateStates()
    {
        foreach (KeyValuePair<string, HaEntityState> entry in _states)
        {
            yield return entry.Value;
        }
    }

    /// <summary>Counts matching entities in a single pass, without allocating.</summary>
    public int CountStates(Func<HaEntityState, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        int count = 0;

        foreach (KeyValuePair<string, HaEntityState> entry in _states)
        {
            if (predicate(entry.Value))
            {
                count++;
            }
        }

        return count;
    }

    public IReadOnlyList<HaArea> Areas { get; private set; } = [];

    public IReadOnlyList<HaFloor> Floors { get; private set; } = [];

    public IReadOnlyList<HaService> Services { get; private set; } = [];

    public HaEntityState? Find(string entityId) =>
        entityId is not null && _states.TryGetValue(entityId, out HaEntityState? state) ? state : null;

    // ------------------------------------------------------------------ lifecycle

    /// <summary>
    /// Connects, or reconnects with new options. Passing null disconnects and clears the cache.
    /// </summary>
    public async Task ApplyConnectionAsync(HaConnectionOptions? options)
    {
        await TeardownClientAsync().ConfigureAwait(true);

        if (options is null)
        {
            ClearCache();
            ConnectionState = HaConnectionState.Disconnected;
            StatusMessage = "Not configured yet. Add your server address and access token in Settings.";
            return;
        }

        IReadOnlyList<string> problems = options.Validate();
        if (problems.Count > 0)
        {
            ConnectionState = HaConnectionState.Failed;
            StatusMessage = string.Join(" ", problems);
            return;
        }

        _client = new HaClient(options, _log.AsCallback(LogLevel.Debug));
        _client.ConnectionStateChanged += HandleConnectionStateChanged;
        _client.StateChanged += HandleStateChanged;
        _client.Resynchronised += HandleResynchronised;

        _log.Info($"Connecting to Home Assistant at {options.BaseUrl}.");
        _client.Start();
    }

    /// <summary>Drops the connection and immediately reconnects with the same options.</summary>
    public async Task ReconnectAsync()
    {
        HaConnectionOptions? options = _client?.Options;

        if (options is null)
        {
            return;
        }

        await ApplyConnectionAsync(options).ConfigureAwait(true);
    }

    private async Task TeardownClientAsync()
    {
        if (_client is null)
        {
            return;
        }

        _client.ConnectionStateChanged -= HandleConnectionStateChanged;
        _client.StateChanged -= HandleStateChanged;
        _client.Resynchronised -= HandleResynchronised;

        HaClient client = _client;
        _client = null;

        // Cancel any snapshot still in flight against the old connection.
        if (_snapshotCancellation is not null)
        {
            await _snapshotCancellation.CancelAsync().ConfigureAwait(false);
            _snapshotCancellation.Dispose();
            _snapshotCancellation = null;
        }

        await client.DisposeAsync().ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ client events

    private void HandleConnectionStateChanged(object? sender, HaConnectionStateChangedEventArgs e)
    {
        Post(() =>
        {
            ConnectionState = e.State;
            ServerVersion = _client?.ServerVersion;

            StatusMessage = e.Detail ?? DescribeState(e.State);

            if (e.State is HaConnectionState.Failed or HaConnectionState.Disconnected)
            {
                IsLoadingSnapshot = false;
            }

            OnPropertyChanged(nameof(IsReady));
        });
    }

    private void HandleStateChanged(object? sender, HaStateChangedEventArgs e)
    {
        if (e.NewState is null)
        {
            _states.TryRemove(e.EntityId, out _);
        }
        else
        {
            _states[e.EntityId] = e.NewState;
        }

        // Raised on the UI thread, but the cache above is updated immediately so that a view model
        // reading it during the same frame never sees a stale value.
        Post(() => EntityChanged?.Invoke(this, new EntityChangedEventArgs(e.EntityId, e.NewState)));
    }

    private void HandleResynchronised(object? sender, EventArgs e)
    {
        HaClient? client = _client;

        if (client is null)
        {
            return;
        }

        _snapshotCancellation?.Cancel();
        _snapshotCancellation?.Dispose();
        _snapshotCancellation = new CancellationTokenSource();

        _ = LoadSnapshotAsync(client, _snapshotCancellation.Token);
    }

    /// <summary>Reads the full state machine and the registries after every successful connect.</summary>
    private async Task LoadSnapshotAsync(HaClient client, CancellationToken ct)
    {
        Post(() => IsLoadingSnapshot = true);

        try
        {
            IReadOnlyList<HaEntityState> states = await client.GetStatesAsync(ct).ConfigureAwait(false);

            // The registries are what turn a flat list of entity ids into rooms. Failing to read
            // them is survivable — the UI falls back to grouping by domain — so they are gathered
            // individually rather than as one all-or-nothing batch.
            IReadOnlyList<HaArea> areas = await SafelyReadAsync(
                () => client.GetAreasAsync(ct), "areas", []).ConfigureAwait(false);

            IReadOnlyList<HaFloor> floors = await SafelyReadAsync(
                () => client.GetFloorsAsync(ct), "floors", []).ConfigureAwait(false);

            IReadOnlyList<HaDevice> devices = await SafelyReadAsync(
                () => client.GetDevicesAsync(ct), "devices", []).ConfigureAwait(false);

            IReadOnlyList<HaEntityRegistryEntry> registry = await SafelyReadAsync(
                () => client.GetEntityRegistryAsync(ct), "the entity registry", []).ConfigureAwait(false);

            IReadOnlyList<HaService> services = await SafelyReadAsync(
                () => client.GetServicesAsync(ct), "the service list", []).ConfigureAwait(false);

            HaConfig? config = await SafelyReadAsync<HaConfig?>(
                () => client.GetConfigAsync(ct), "the server configuration", null).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            _states.Clear();
            foreach (HaEntityState state in states)
            {
                _states[state.EntityId] = state;
            }

            Post(() =>
            {
                Areas = areas;
                Floors = floors;
                Services = services;

                _areasById.Clear();
                foreach (HaArea area in areas)
                {
                    _areasById[area.AreaId] = area;
                }

                _floorsById.Clear();
                foreach (HaFloor floor in floors)
                {
                    _floorsById[floor.FloorId] = floor;
                }

                _devicesById.Clear();
                foreach (HaDevice device in devices)
                {
                    _devicesById[device.Id] = device;
                }

                _registryByEntity.Clear();
                foreach (HaEntityRegistryEntry entry in registry)
                {
                    _registryByEntity[entry.EntityId] = entry;
                }

                LocationName = config?.LocationName;
                IsLoadingSnapshot = false;

                _log.Info(
                    $"Loaded {states.Count} entities, {areas.Count} areas, {devices.Count} devices.");

                OnPropertyChanged(nameof(IsReady));
                SnapshotReloaded?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer connection.
        }
        catch (Exception ex)
        {
            _log.Error("Could not load the Home Assistant snapshot.", ex);

            Post(() =>
            {
                IsLoadingSnapshot = false;
                StatusMessage = "Connected, but the entity list could not be read. Retrying shortly.";
            });
        }
    }

    private async Task<T> SafelyReadAsync<T>(Func<Task<T>> read, string what, T fallback)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not read {what} from Home Assistant; continuing without it.", ex);
            return fallback;
        }
    }

    // ------------------------------------------------------------------ commands

    /// <summary>
    /// Runs a command against the connection, turning any failure into a message worth showing.
    /// </summary>
    public async Task<CommandResult> ExecuteAsync(
        Func<HaClient, CancellationToken, Task> action,
        string description,
        CancellationToken ct = default)
    {
        HaClient? client = _client;

        if (client is null || ConnectionState != HaConnectionState.Connected)
        {
            return CommandResult.Failed("Not connected to Home Assistant.");
        }

        try
        {
            await action(client, ct).ConfigureAwait(true);
            _log.Debug($"{description} succeeded.");
            return CommandResult.Ok;
        }
        catch (HaCommandException ex)
        {
            _log.Warning($"{description} was refused by Home Assistant.", ex);
            return CommandResult.Failed(ex.ServerMessage);
        }
        catch (HaConnectionException ex)
        {
            _log.Warning($"{description} failed because the connection dropped.", ex);
            return CommandResult.Failed("The connection dropped. Reconnecting…");
        }
        catch (OperationCanceledException)
        {
            return CommandResult.Failed("Timed out waiting for Home Assistant.");
        }
        catch (Exception ex)
        {
            _log.Error($"{description} failed unexpectedly.", ex);
            return CommandResult.Failed(ex.Message);
        }
    }

    // ------------------------------------------------------------------ registry lookups

    /// <summary>
    /// The area an entity belongs to.
    /// </summary>
    /// <remarks>
    /// An entity's own area assignment wins; otherwise it inherits the area of its device. That is
    /// the same resolution order Home Assistant's own UI uses, and getting it backwards puts
    /// entities in the wrong room for anybody who has overridden one.
    /// </remarks>
    public HaArea? AreaFor(string entityId)
    {
        if (!_registryByEntity.TryGetValue(entityId, out HaEntityRegistryEntry? entry))
        {
            return null;
        }

        if (entry.AreaId is { Length: > 0 } direct
            && _areasById.TryGetValue(direct, out HaArea? area))
        {
            return area;
        }

        if (entry.DeviceId is { Length: > 0 } deviceId
            && _devicesById.TryGetValue(deviceId, out HaDevice? device)
            && device.AreaId is { Length: > 0 } inherited
            && _areasById.TryGetValue(inherited, out HaArea? deviceArea))
        {
            return deviceArea;
        }

        return null;
    }

    public HaFloor? FloorFor(string entityId)
    {
        HaArea? area = AreaFor(entityId);

        return area?.FloorId is { Length: > 0 } floorId
               && _floorsById.TryGetValue(floorId, out HaFloor? floor)
            ? floor
            : null;
    }

    public HaEntityRegistryEntry? RegistryEntryFor(string entityId) =>
        _registryByEntity.TryGetValue(entityId, out HaEntityRegistryEntry? entry) ? entry : null;

    /// <summary>
    /// Entities worth offering in the browser: supported domain, not disabled, not hidden.
    /// </summary>
    /// <param name="includeAuxiliary">Include config and diagnostic entities.</param>
    /// <param name="includeUnavailable">Include entities the server cannot currently reach.</param>
    public IEnumerable<HaEntityState> Browsable(bool includeAuxiliary, bool includeUnavailable)
    {
        foreach (KeyValuePair<string, HaEntityState> pair in _states)
        {
            HaEntityState state = pair.Value;

            if (!HaDomains.IsSupported(state.Domain))
            {
                continue;
            }

            if (!includeUnavailable && state.IsUnavailable)
            {
                continue;
            }

            HaEntityRegistryEntry? entry = RegistryEntryFor(state.EntityId);

            if (entry is not null)
            {
                if (entry.IsDisabled || entry.IsHidden)
                {
                    continue;
                }

                if (!includeAuxiliary && entry.IsAuxiliary)
                {
                    continue;
                }
            }

            yield return state;
        }
    }

    // ------------------------------------------------------------------ plumbing

    private void ClearCache()
    {
        _states.Clear();
        _areasById.Clear();
        _floorsById.Clear();
        _devicesById.Clear();
        _registryByEntity.Clear();

        Areas = [];
        Floors = [];
        Services = [];
        ServerVersion = null;
        LocationName = null;

        SnapshotReloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Marshals onto the UI thread, running inline when already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Queued at <see cref="DispatcherPriority.Background"/>, which is deliberately below both
    /// <c>Render</c> and <c>Input</c>. This used to be <c>DataBind</c> — a <em>higher</em> priority
    /// than rendering — and a burst of <c>state_changed</c> events from a busy server would
    /// therefore starve the render loop and freeze the interface until the burst cleared. A house
    /// with a thousand entities produces such bursts constantly.
    /// </para>
    /// <para>
    /// The cost is that a state change may be applied a frame or two late. That is invisible; a
    /// stalled window is not.
    /// </para>
    /// </remarks>
    private void Post(Action action)
    {
        if (_disposed)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Background, action);
    }

    private static string DescribeState(HaConnectionState state) => state switch
    {
        HaConnectionState.Disconnected => "Disconnected.",
        HaConnectionState.Connecting => "Connecting…",
        HaConnectionState.Authenticating => "Authenticating…",
        HaConnectionState.Connected => "Connected.",
        HaConnectionState.Reconnecting => "Connection lost. Reconnecting…",
        HaConnectionState.Failed => "Could not connect.",
        _ => string.Empty,
    };

    partial void OnConnectionStateChanged(HaConnectionState value) =>
        OnPropertyChanged(nameof(IsReady));

    partial void OnIsLoadingSnapshotChanged(bool value) =>
        OnPropertyChanged(nameof(IsReady));

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await TeardownClientAsync().ConfigureAwait(false);
    }
}
