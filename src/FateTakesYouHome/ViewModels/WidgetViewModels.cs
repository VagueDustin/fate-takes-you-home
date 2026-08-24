using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FateTakesYouHome.HomeAssistant.Models;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;

namespace FateTakesYouHome.ViewModels;

/// <summary>
/// One widget as rendered on the home screen or the tray panel.
/// </summary>
/// <remarks>
/// Placement is fixed at load: rearranging happens in the layout editor against the specs, and the
/// surfaces rebuild from settings when a layout is saved. Keeping the runtime widgets dumb about
/// editing is what keeps the render path identical whether or not the user ever customises.
/// </remarks>
public abstract partial class WidgetViewModel(WidgetSpec spec) : ObservableObject, IDisposable
{
    public WidgetSpec Spec { get; } = spec;

    public WidgetKind Kind => Spec.Kind;

    public int X => Spec.X;

    public int Y => Spec.Y;

    public int W => Spec.W;

    public int H => Spec.H;

    public virtual void Dispose()
    {
    }
}

/// <summary>An entity tile in widget clothing. The tile itself is the shared control.</summary>
public sealed class TileWidgetViewModel : WidgetViewModel
{
    public TileWidgetViewModel(WidgetSpec spec, EntityTileViewModel tile)
        : base(spec)
    {
        Tile = tile;
    }

    public EntityTileViewModel Tile { get; }

    public override void Dispose() => Tile.Detach();
}

/// <summary>One button, one action.</summary>
public sealed partial class QuickActionWidgetViewModel : WidgetViewModel
{
    private readonly HomeAssistantService _homeAssistant;
    private readonly EntityTileViewModel? _entity;

    [ObservableProperty]
    private string? _lastResult;

    public QuickActionWidgetViewModel(
        WidgetSpec spec, HomeAssistantService homeAssistant, EntityTileViewModel? entity)
        : base(spec)
    {
        _homeAssistant = homeAssistant;
        _entity = entity;
    }

    public string Label => _entity?.DisplayName
        ?? (Spec.Action == WidgetSpec.AllLightsOffAction ? "All lights off" : "Quick action");

    public Geometry? Glyph => _entity is not null
        ? _entity.Glyph
        : System.Windows.Application.Current?.TryFindResource("Fate.Icon.Power") as Geometry;

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (_entity is not null)
        {
            _entity.PrimaryCommand.Execute(null);
            return;
        }

        if (Spec.Action == WidgetSpec.AllLightsOffAction)
        {
            CommandResult result =
                await _homeAssistant.TurnOffAllLightsAsync().ConfigureAwait(true);
            LastResult = result.Succeeded ? "Done." : result.ErrorMessage;
        }
    }

    public override void Dispose() => _entity?.Detach();
}

/// <summary>The rooms grid, as a widget. Shares the dashboard's live collection.</summary>
public sealed class RoomsWidgetViewModel(WidgetSpec spec, ObservableCollection<RoomSummary> rooms)
    : WidgetViewModel(spec)
{
    public ObservableCollection<RoomSummary> Rooms { get; } = rooms;
}

/// <summary>The activity counters, as a widget. Shares the dashboard's live collection.</summary>
public sealed class ActivityWidgetViewModel(WidgetSpec spec, ObservableCollection<ActivitySummary> activity)
    : WidgetViewModel(spec)
{
    public ObservableCollection<ActivitySummary> Activity { get; } = activity;
}

/// <summary>
/// A little history chart of one numeric entity — temperature through the day, power draw, CO₂.
/// </summary>
public sealed partial class SparklineWidgetViewModel : WidgetViewModel
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(10);

    private readonly HomeAssistantService _homeAssistant;
    private readonly DispatcherTimer _refresh;
    private bool _disposed;

    [ObservableProperty]
    private PointCollection _points = [];

    [ObservableProperty]
    private string _currentText = "—";

    [ObservableProperty]
    private string _rangeText = "";

    [ObservableProperty]
    private bool _hasData;

    public SparklineWidgetViewModel(WidgetSpec spec, HomeAssistantService homeAssistant)
        : base(spec)
    {
        _homeAssistant = homeAssistant;

        HaEntityState? state = spec.EntityId is not null ? homeAssistant.Find(spec.EntityId) : null;
        Name = state?.FriendlyName ?? spec.EntityId ?? "History";
        Unit = state?.AttrString("unit_of_measurement") ?? string.Empty;

        _refresh = new DispatcherTimer { Interval = RefreshEvery };
        _refresh.Tick += async (_, _) => await LoadAsync().ConfigureAwait(true);
        _refresh.Start();

        _ = LoadAsync();
    }

    public string Name { get; }

    public string Unit { get; }

    public string WindowText => Spec.Hours >= 48 ? $"{Spec.Hours / 24} days" : $"{Spec.Hours} h";

    private async Task LoadAsync()
    {
        if (Spec.EntityId is not { } entityId)
        {
            return;
        }

        IReadOnlyList<HaHistoryPoint> history =
            await _homeAssistant.FetchHistoryAsync(entityId, TimeSpan.FromHours(Spec.Hours))
                .ConfigureAwait(true);

        if (_disposed)
        {
            return;
        }

        if (history.Count < 2)
        {
            HasData = false;
            CurrentText = _homeAssistant.Find(entityId)?.State ?? "—";
            return;
        }

        double min = double.MaxValue, max = double.MinValue;

        foreach (HaHistoryPoint point in history)
        {
            min = Math.Min(min, point.Value);
            max = Math.Max(max, point.Value);
        }

        // A flat line still deserves to be visible in the middle rather than on the floor.
        double span = Math.Max(max - min, 0.0001);
        bool flat = max - min < 0.0001;

        DateTimeOffset start = history[0].Time;
        double seconds = Math.Max(1, (history[^1].Time - start).TotalSeconds);

        // Drawn into a fixed 100×40 space and stretched by the view; WPF scales geometry for free.
        var points = new PointCollection();

        foreach (HaHistoryPoint point in history)
        {
            double x = (point.Time - start).TotalSeconds / seconds * 100;
            double y = flat ? 20 : 38 - ((point.Value - min) / span * 36);
            points.Add(new System.Windows.Point(x, y));
        }

        points.Freeze();

        Points = points;
        HasData = true;
        CurrentText = FormatValue(history[^1].Value);
        RangeText = $"{FormatValue(min)} – {FormatValue(max)}";
    }

    private string FormatValue(double value)
    {
        string number = Math.Abs(value) >= 100
            ? value.ToString("0")
            : value.ToString("0.#");

        return Unit.Length > 0 ? $"{number} {Unit}" : number;
    }

    public override void Dispose()
    {
        _disposed = true;
        _refresh.Stop();
    }
}

/// <summary>Builds runtime widgets from their specs.</summary>
public static class WidgetFactory
{
    /// <summary>
    /// Creates the view model for one spec, or null when the spec cannot be realised here —
    /// an entity that no longer exists, or a rooms widget on a surface with no room data.
    /// </summary>
    public static WidgetViewModel? Build(
        WidgetSpec spec,
        HomeAssistantService homeAssistant,
        ObservableCollection<RoomSummary>? rooms,
        ObservableCollection<ActivitySummary>? activity,
        string? pinLabel = null)
    {
        switch (spec.Kind)
        {
            case WidgetKind.Tile when spec.EntityId is not null:
                return homeAssistant.Find(spec.EntityId) is { } state
                    ? new TileWidgetViewModel(spec, new EntityTileViewModel(state, homeAssistant, pinLabel))
                    : null;

            case WidgetKind.QuickAction:
                EntityTileViewModel? entity = spec.EntityId is not null
                    && homeAssistant.Find(spec.EntityId) is { } target
                        ? new EntityTileViewModel(target, homeAssistant, pinLabel)
                        : null;

                if (entity is null && spec.Action is not WidgetSpec.AllLightsOffAction)
                {
                    return null;
                }

                return new QuickActionWidgetViewModel(spec, homeAssistant, entity);

            case WidgetKind.Rooms when rooms is not null:
                return new RoomsWidgetViewModel(spec, rooms);

            case WidgetKind.Activity when activity is not null:
                return new ActivityWidgetViewModel(spec, activity);

            case WidgetKind.Sparkline when spec.EntityId is not null:
                return new SparklineWidgetViewModel(spec, homeAssistant);

            default:
                return null;
        }
    }
}
