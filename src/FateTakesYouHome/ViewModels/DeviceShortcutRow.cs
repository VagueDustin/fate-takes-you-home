// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.HomeAssistant.Models;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;

namespace FateTakesYouHome.ViewModels;

/// <summary>One entry in the action picker.</summary>
public sealed record DeviceActionChoice(DeviceAction Value, string Label);

/// <summary>A device a shortcut acts on, as a removable chip.</summary>
/// <param name="IsSkipped">True when the chosen action does nothing to this device.</param>
public sealed record DeviceShortcutTarget(string EntityId, string Name, bool IsSkipped);

/// <summary>A search result in the device picker.</summary>
public sealed record DeviceShortcutMatch(string EntityId, string Name, string Detail);

/// <summary>
/// One device shortcut, as an editable row on the appearance page.
/// </summary>
/// <remarks>
/// Edits write straight through to the <see cref="DeviceShortcut"/> held in settings and are saved
/// as they happen, the same as the application shortcuts above it. There is no separate Save for
/// a shortcut, because a recorded key that does nothing until a button is found is a trap.
/// </remarks>
public sealed partial class DeviceShortcutRow : ObservableObject
{
    private const int MaxMatches = 8;

    private readonly HomeAssistantService _homeAssistant;
    private readonly Action _onChanged;
    private readonly Action<DeviceShortcutRow> _onRemove;
    private readonly bool _loading;

    [ObservableProperty]
    private string? _gesture;

    [ObservableProperty]
    private DeviceAction _action;

    [ObservableProperty]
    private double _value;

    [ObservableProperty]
    private string? _failure;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private DeviceShortcutMatch? _selectedMatch;

    public DeviceShortcutRow(
        DeviceShortcut model,
        HomeAssistantService homeAssistant,
        Action onChanged,
        Action<DeviceShortcutRow> onRemove)
    {
        Model = model;
        _homeAssistant = homeAssistant;
        _onChanged = onChanged;
        _onRemove = onRemove;

        _loading = true;
        Gesture = model.Gesture;
        Action = model.Action;
        Value = model.Value ?? DeviceActions.DefaultValue(model.Action);
        _loading = false;

        RebuildTargets();
    }

    /// <summary>Every action, in the order the picker lists them.</summary>
    public static IReadOnlyList<DeviceActionChoice> Actions { get; } = Enum.GetValues<DeviceAction>()
        .Select(action => new DeviceActionChoice(action, DeviceShortcut.ActionLabel(action)))
        .ToList();

    /// <summary>The settings object this row edits.</summary>
    public DeviceShortcut Model { get; }

    public ObservableCollection<DeviceShortcutTarget> Targets { get; } = [];

    public ObservableCollection<DeviceShortcutMatch> Matches { get; } = [];

    public bool TakesValue => DeviceActions.TakesValue(Action);

    public string ValueLabel => Action == DeviceAction.SetBrightness ? "level" : "step";

    /// <summary>A level may be 0 (off); a step of 0 would do nothing, so the slider stops at 5.</summary>
    public double ValueMinimum => Action == DeviceAction.SetBrightness ? 0 : 5;

    /// <summary>"Toggle Desk lamp": the row's accessible name, and how clashes refer to it.</summary>
    public string Summary => Model.Describe(NameOf);

    /// <summary>Says which targets the action will skip, and why, when any will.</summary>
    public string? Note
    {
        get
        {
            int skipped = Targets.Count(t => t.IsSkipped);

            if (skipped == 0)
            {
                return null;
            }

            string label = DeviceShortcut.ActionLabel(Action);

            string note = skipped == Targets.Count
                ? $"{label} does nothing to {(skipped == 1 ? "this device" : "any of these devices")}."
                : $"{label} skips {skipped} of these {Targets.Count} devices.";

            if (Action == DeviceAction.TurnOn && Targets.Any(t => DomainOf(t.EntityId) == HaDomains.Lock))
            {
                note += " A shortcut can lock a door, but never unlock one.";
            }

            return note;
        }
    }

    partial void OnGestureChanged(string? value)
    {
        Model.Gesture = value;
        Changed();
    }

    partial void OnActionChanged(DeviceAction value)
    {
        Model.Action = value;

        // A step of 10 and a level of 10 mean very different things, so a value carried across
        // from the previous action would be a surprise. Start from the new action's default.
        if (!_loading)
        {
            Value = DeviceActions.DefaultValue(value);
        }

        RebuildTargets();
        OnPropertyChanged(nameof(TakesValue));
        OnPropertyChanged(nameof(ValueLabel));
        OnPropertyChanged(nameof(ValueMinimum));
        Changed();
    }

    partial void OnValueChanged(double value)
    {
        if (_loading)
        {
            return;
        }

        Model.Value = DeviceActions.ClampValue(Action, value);
        Changed();
    }

    partial void OnSearchTextChanged(string value) => RefreshMatches();

    /// <summary>
    /// Adds a device. With no argument, adds the highlighted search result, or the first one, so
    /// Enter in the search box does the obvious thing.
    /// </summary>
    [RelayCommand]
    private void AddTarget(DeviceShortcutMatch? match)
    {
        match ??= SelectedMatch ?? Matches.FirstOrDefault();

        if (match is null || Model.EntityIds.Contains(match.EntityId, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Model.EntityIds.Add(match.EntityId);
        SearchText = string.Empty;
        RebuildTargets();
        Changed();
    }

    [RelayCommand]
    private void RemoveTarget(DeviceShortcutTarget? target)
    {
        if (target is null)
        {
            return;
        }

        Model.EntityIds.RemoveAll(id => string.Equals(id, target.EntityId, StringComparison.OrdinalIgnoreCase));
        RebuildTargets();
        RefreshMatches();
        Changed();
    }

    [RelayCommand]
    private void Remove() => _onRemove(this);

    /// <summary>Re-reads names after Home Assistant reloads, so chips stop showing raw entity ids.</summary>
    public void RefreshNames()
    {
        RebuildTargets();
        RefreshMatches();
    }

    /// <summary>Reloads the failure state without re-triggering a save.</summary>
    public void SetFailureQuietly(string? failure) => Failure = failure;

    private void Changed()
    {
        OnPropertyChanged(nameof(Summary));

        if (!_loading)
        {
            _onChanged();
        }
    }

    private void RebuildTargets()
    {
        Targets.Clear();

        foreach (string entityId in Model.EntityIds)
        {
            Targets.Add(new DeviceShortcutTarget(
                entityId,
                NameOf(entityId),
                !DeviceActions.AppliesTo(Action, DomainOf(entityId))));
        }

        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(Summary));
    }

    private void RefreshMatches()
    {
        Matches.Clear();

        string query = SearchText.Trim();

        if (query.Length == 0)
        {
            return;
        }

        IEnumerable<HaEntityState> found = _homeAssistant
            .Browsable(includeAuxiliary: false, includeUnavailable: true)
            .Where(state => DeviceActions.IsTargetable(state.Domain))
            .Where(state => !Model.EntityIds.Contains(state.EntityId, StringComparer.OrdinalIgnoreCase))
            .Where(state =>
                state.FriendlyName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || state.EntityId.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(state => state.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxMatches);

        foreach (HaEntityState state in found)
        {
            string detail = _homeAssistant.AreaFor(state.EntityId) is { } area
                ? $"{area.Name} · {state.EntityId}"
                : state.EntityId;

            Matches.Add(new DeviceShortcutMatch(state.EntityId, state.FriendlyName, detail));
        }

        SelectedMatch = Matches.FirstOrDefault();
    }

    private string NameOf(string entityId) =>
        _homeAssistant.Find(entityId)?.FriendlyName ?? entityId;

    private static string DomainOf(string entityId)
    {
        int dot = entityId.IndexOf('.');
        return dot > 0 ? entityId[..dot] : entityId;
    }
}
