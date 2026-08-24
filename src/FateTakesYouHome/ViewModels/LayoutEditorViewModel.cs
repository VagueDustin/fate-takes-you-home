using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FateTakesYouHome.HomeAssistant.Models;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;

namespace FateTakesYouHome.ViewModels;

/// <summary>One widget on the editor canvas: a spec with observable placement.</summary>
public sealed partial class EditorWidget : ObservableObject
{
    [ObservableProperty]
    private int _x;

    [ObservableProperty]
    private int _y;

    [ObservableProperty]
    private int _w;

    [ObservableProperty]
    private int _h;

    /// <summary>True while a drag has parked it somewhere it cannot stay.</summary>
    [ObservableProperty]
    private bool _isInvalid;

    public EditorWidget(WidgetSpec spec, string title, string subtitle)
    {
        Spec = spec;
        Title = title;
        Subtitle = subtitle;
        _x = spec.X;
        _y = spec.Y;
        _w = spec.W;
        _h = spec.H;
    }

    public WidgetSpec Spec { get; }

    public WidgetKind Kind => Spec.Kind;

    public string Title { get; }

    public string Subtitle { get; }

    /// <summary>Writes the live placement back into the spec, for saving.</summary>
    public WidgetSpec Commit()
    {
        Spec.X = X;
        Spec.Y = Y;
        Spec.W = W;
        Spec.H = H;
        return Spec;
    }

    public bool Overlaps(EditorWidget other) =>
        !ReferenceEquals(this, other)
        && X < other.X + other.W && other.X < X + W
        && Y < other.Y + other.H && other.Y < Y + H;
}

/// <summary>An entity offered by the picker overlay.</summary>
public sealed record PickerEntity(string EntityId, string Name, string Detail);

/// <summary>
/// The layout editor: arrange, resize, add and remove the widgets on the home screen and the
/// tray panel, phone-launcher style.
/// </summary>
/// <remarks>
/// The editor works on cheap stand-in cards rather than live widgets, so arranging never fires an
/// action, and it commits only on Save — walking away loses nothing but the arrangement attempt.
/// </remarks>
public sealed partial class LayoutEditorViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly HomeAssistantService _homeAssistant;

    [ObservableProperty]
    private bool _editingFlyout;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    // -- picker state ----------------------------------------------------------------------------

    [ObservableProperty]
    private bool _isPicking;

    [ObservableProperty]
    private string _pickerQuery = string.Empty;

    [ObservableProperty]
    private PickerEntity? _pickerSelection;

    private WidgetKind _pendingKind = WidgetKind.Tile;

    public LayoutEditorViewModel(SettingsService settings, HomeAssistantService homeAssistant)
    {
        _settings = settings;
        _homeAssistant = homeAssistant;

        Load();
    }

    public ObservableCollection<EditorWidget> Items { get; } = [];

    public ObservableCollection<PickerEntity> PickerResults { get; } = [];

    public int Columns => EditingFlyout
        ? FlyoutViewModel.FlyoutGridColumns
        : DashboardViewModel.HomeGridColumns;

    public double RowHeight => EditingFlyout ? 72 : 84;

    public string SurfaceExplainer => EditingFlyout
        ? "Four columns, sized like the tray panel. What you arrange here is what a single click on the tray icon opens."
        : "Six columns that stretch with the window. This replaces the standard pinned-and-rooms view once saved.";

    /// <summary>Rooms and activity tallies only exist on the home screen.</summary>
    public bool AllowsSummaryWidgets => !EditingFlyout;

    partial void OnEditingFlyoutChanged(bool value)
    {
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(RowHeight));
        OnPropertyChanged(nameof(SurfaceExplainer));
        OnPropertyChanged(nameof(AllowsSummaryWidgets));
        Load();
    }

    partial void OnPickerQueryChanged(string value) => RefreshPickerResults();

    // ------------------------------------------------------------------ load / save

    /// <summary>Loads the saved layout, or a generated equivalent of the standard view.</summary>
    public void Load()
    {
        Items.Clear();

        List<WidgetSpec>? saved = EditingFlyout
            ? _settings.Current.FlyoutWidgets
            : _settings.Current.HomeWidgets;

        List<WidgetSpec> specs = saved is { Count: > 0 }
            ? saved.Select(s => s.Clone()).ToList()
            : GenerateDefault();

        foreach (WidgetSpec spec in specs)
        {
            spec.ClampTo(Columns);
            Items.Add(Realise(spec));
        }

        HasUnsavedChanges = false;
        StatusMessage = null;
    }

    /// <summary>The standard view, expressed as widgets, as a starting arrangement.</summary>
    private List<WidgetSpec> GenerateDefault()
    {
        var specs = new List<WidgetSpec>();
        int columns = Columns;
        int tileWidth = EditingFlyout ? columns : columns / 2;
        int x = 0, y = 0;

        if (!EditingFlyout)
        {
            specs.Add(new WidgetSpec { Kind = WidgetKind.Activity, X = 0, Y = 0, W = columns, H = 1 });
            y = 1;
        }

        foreach (PinnedEntity pin in _settings.Current.Pinned)
        {
            specs.Add(new WidgetSpec
            {
                Kind = WidgetKind.Tile,
                EntityId = pin.EntityId,
                X = x,
                Y = y,
                W = tileWidth,
                H = 1,
            });

            x += tileWidth;

            if (x + tileWidth > columns)
            {
                x = 0;
                y++;
            }
        }

        if (x != 0)
        {
            x = 0;
            y++;
        }

        if (EditingFlyout)
        {
            specs.Add(new WidgetSpec
            {
                Kind = WidgetKind.QuickAction,
                Action = WidgetSpec.AllLightsOffAction,
                X = 0,
                Y = y,
                W = 2,
                H = 1,
            });
        }
        else
        {
            specs.Add(new WidgetSpec { Kind = WidgetKind.Rooms, X = 0, Y = y, W = columns, H = 2 });
        }

        return specs;
    }

    private EditorWidget Realise(WidgetSpec spec)
    {
        (string title, string subtitle) = spec.Kind switch
        {
            WidgetKind.Tile => (EntityName(spec.EntityId), "Entity tile"),
            WidgetKind.Sparkline => (EntityName(spec.EntityId), $"History · {spec.Hours} h"),
            WidgetKind.QuickAction when spec.Action == WidgetSpec.AllLightsOffAction =>
                ("All lights off", "Quick action"),
            WidgetKind.QuickAction => (EntityName(spec.EntityId), "Quick action"),
            WidgetKind.Rooms => ("Rooms", "Every area with lights"),
            WidgetKind.Activity => ("Activity", "Whole-house counts"),
            _ => ("Widget", spec.Kind.ToString()),
        };

        return new EditorWidget(spec, title, subtitle);
    }

    private string EntityName(string? entityId)
    {
        if (entityId is null)
        {
            return "Entity";
        }

        string? label = _settings.Current.Pinned
            .FirstOrDefault(p => string.Equals(p.EntityId, entityId, StringComparison.OrdinalIgnoreCase))?
            .Label;

        if (label is { Length: > 0 })
        {
            return label;
        }

        return _homeAssistant.Find(entityId)?.FriendlyName ?? entityId;
    }

    [RelayCommand]
    private void Save()
    {
        List<WidgetSpec> specs = Items.Select(item => item.Commit()).ToList();

        foreach (WidgetSpec spec in specs)
        {
            spec.ClampTo(Columns);
        }

        if (EditingFlyout)
        {
            _settings.Current.FlyoutWidgets = specs;
        }
        else
        {
            _settings.Current.HomeWidgets = specs;
        }

        _settings.Save();
        _settings.NotifyChanged();

        HasUnsavedChanges = false;
        StatusMessage = "Saved. The layout is live.";
    }

    [RelayCommand]
    private void ResetToStandard()
    {
        if (EditingFlyout)
        {
            _settings.Current.FlyoutWidgets = null;
        }
        else
        {
            _settings.Current.HomeWidgets = null;
        }

        _settings.Save();
        _settings.NotifyChanged();
        Load();
        StatusMessage = "Back to the standard layout.";
    }

    [RelayCommand]
    private void Discard() => Load();

    // ------------------------------------------------------------------ adding and removing

    [RelayCommand]
    private void Remove(EditorWidget? widget)
    {
        if (widget is not null && Items.Remove(widget))
        {
            HasUnsavedChanges = true;
        }
    }

    [RelayCommand]
    private void AddAllLightsOff() =>
        Place(new WidgetSpec { Kind = WidgetKind.QuickAction, Action = WidgetSpec.AllLightsOffAction, W = 2, H = 1 });

    [RelayCommand]
    private void AddRooms() => Place(new WidgetSpec { Kind = WidgetKind.Rooms, W = Columns, H = 2 });

    [RelayCommand]
    private void AddActivity() => Place(new WidgetSpec { Kind = WidgetKind.Activity, W = Columns, H = 1 });

    [RelayCommand]
    private void BeginAddTile() => BeginPick(WidgetKind.Tile);

    [RelayCommand]
    private void BeginAddSparkline() => BeginPick(WidgetKind.Sparkline);

    [RelayCommand]
    private void BeginAddQuickAction() => BeginPick(WidgetKind.QuickAction);

    private void BeginPick(WidgetKind kind)
    {
        _pendingKind = kind;
        PickerQuery = string.Empty;
        PickerSelection = null;
        RefreshPickerResults();
        IsPicking = true;
    }

    [RelayCommand]
    private void CancelPick() => IsPicking = false;

    [RelayCommand]
    private void ConfirmPick()
    {
        if (PickerSelection is not { } picked)
        {
            return;
        }

        IsPicking = false;

        WidgetSpec spec = _pendingKind switch
        {
            WidgetKind.Sparkline => new WidgetSpec
            {
                Kind = WidgetKind.Sparkline,
                EntityId = picked.EntityId,
                W = Math.Min(3, Columns),
                H = 2,
            },
            WidgetKind.QuickAction => new WidgetSpec
            {
                Kind = WidgetKind.QuickAction,
                EntityId = picked.EntityId,
                W = 2,
                H = 1,
            },
            _ => new WidgetSpec
            {
                Kind = WidgetKind.Tile,
                EntityId = picked.EntityId,
                W = Math.Min(EditingFlyout ? Columns : 3, Columns),
                H = 1,
            },
        };

        Place(spec);
    }

    private void Place(WidgetSpec spec)
    {
        (spec.X, spec.Y) = FirstFreeSlot(spec.W, spec.H);
        Items.Add(Realise(spec));
        HasUnsavedChanges = true;
    }

    /// <summary>The topmost, leftmost place a widget of this size fits without overlap.</summary>
    private (int X, int Y) FirstFreeSlot(int w, int h)
    {
        var probe = new EditorWidget(new WidgetSpec { W = w, H = h }, "", "");

        for (int y = 0; y < 200; y++)
        {
            for (int x = 0; x + w <= Columns; x++)
            {
                probe.X = x;
                probe.Y = y;

                if (!Items.Any(item => item.Overlaps(probe)))
                {
                    return (x, y);
                }
            }
        }

        return (0, 0);
    }

    /// <summary>True when the widget currently collides with any other.</summary>
    public bool CollidesWithAnything(EditorWidget widget) => Items.Any(widget.Overlaps);

    /// <summary>Called by the page after a completed drag or resize.</summary>
    public void MarkDirty() => HasUnsavedChanges = true;

    private void RefreshPickerResults()
    {
        PickerResults.Clear();

        string query = PickerQuery.Trim();
        int added = 0;

        foreach (HaEntityState state in _homeAssistant.EnumerateStates())
        {
            if (added >= 30)
            {
                break;
            }

            if (query.Length > 0
                && !state.FriendlyName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                && !state.EntityId.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string area = _homeAssistant.AreaFor(state.EntityId)?.Name ?? state.Domain;
            PickerResults.Add(new PickerEntity(state.EntityId, state.FriendlyName, area));
            added++;
        }
    }
}
