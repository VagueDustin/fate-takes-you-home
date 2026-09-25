// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;

namespace FateTakesYouHome.ViewModels;

/// <summary>One of the application's own system-wide shortcuts, as a row on the shortcuts page.</summary>
public sealed partial class ShortcutRow : ObservableObject
{
    private readonly Action _onChanged;
    private bool _loading;

    [ObservableProperty]
    private string? _gesture;

    [ObservableProperty]
    private string? _failure;

    public ShortcutRow(HotkeyAction action, string label, string description, string? gesture, Action onChanged)
    {
        Action = action;
        Label = label;
        Description = description;
        _loading = true;
        Gesture = gesture;
        _loading = false;
        _onChanged = onChanged;
    }

    public HotkeyAction Action { get; }

    public string Label { get; }

    public string Description { get; }

    partial void OnGestureChanged(string? value)
    {
        if (!_loading)
        {
            _onChanged();
        }
    }

    /// <summary>Reloads the failure state without re-triggering a save.</summary>
    public void SetFailureQuietly(string? failure) => Failure = failure;
}

/// <summary>
/// Every system-wide shortcut: the application's own, and the ones that act on devices.
/// </summary>
/// <remarks>
/// The two kinds share a page because they share a namespace. A combination can belong to only
/// one of them, and a clash is far easier to understand with both lists in view.
/// </remarks>
public sealed partial class ShortcutsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly HomeAssistantService _homeAssistant;
    private bool _disposed;

    public ShortcutsViewModel(SettingsService settings, HomeAssistantService homeAssistant)
    {
        _settings = settings;
        _homeAssistant = homeAssistant;

        _homeAssistant.SnapshotReloaded += OnSnapshotReloaded;

        BuildShortcutRows();
        BuildDeviceShortcutRows();
    }

    // ------------------------------------------------------------------ application shortcuts

    /// <summary>The mappable system-wide shortcuts.</summary>
    public ObservableCollection<ShortcutRow> Shortcuts { get; } = [];

    private void BuildShortcutRows()
    {
        (HotkeyAction Action, string Description)[] rows =
        [
            (HotkeyAction.OpenPanel, "Beside the tray icon, as if you had clicked it. Press again to close it."),
            (HotkeyAction.OpenWindow, "Brings this window up, or to the front."),
            (HotkeyAction.AllLightsOff, "The leaving-the-house key."),
            (HotkeyAction.RunDefaultPin, "Whichever pin is marked as the default action in Settings."),
        ];

        foreach ((HotkeyAction action, string description) in rows)
        {
            string? gesture = _settings.Current.Shortcuts.TryGetValue(action.ToString(), out string? text)
                ? text
                : null;

            var row = new ShortcutRow(action, HotkeyService.Describe(action), description, gesture, SaveShortcuts);
            Shortcuts.Add(row);
        }

        RefreshShortcutFailures();
    }

    private void SaveShortcuts()
    {
        foreach (ShortcutRow row in Shortcuts)
        {
            _settings.Current.Shortcuts[row.Action.ToString()] = row.Gesture;
        }

        _settings.Save();
        App.Current?.ApplyShortcuts();
        RefreshShortcutFailures();
    }

    private void RefreshShortcutFailures()
    {
        if (App.Current is not { } app)
        {
            return;
        }

        foreach (ShortcutRow row in Shortcuts)
        {
            row.SetFailureQuietly(app.Hotkeys.FailureFor(row.Action.ToString()));
        }

        foreach (DeviceShortcutRow row in DeviceShortcuts)
        {
            row.SetFailureQuietly(app.Hotkeys.FailureFor(row.Model.HotkeyKey));
        }
    }

    // ------------------------------------------------------------------ device shortcuts

    /// <summary>Shortcuts that act on chosen devices.</summary>
    public ObservableCollection<DeviceShortcutRow> DeviceShortcuts { get; } = [];

    private void BuildDeviceShortcutRows()
    {
        foreach (DeviceShortcut shortcut in _settings.Current.DeviceShortcuts)
        {
            DeviceShortcuts.Add(NewDeviceShortcutRow(shortcut));
        }

        RefreshShortcutFailures();
    }

    private DeviceShortcutRow NewDeviceShortcutRow(DeviceShortcut shortcut) =>
        new(shortcut, _homeAssistant, SaveDeviceShortcuts, RemoveDeviceShortcut);

    [RelayCommand]
    private void AddDeviceShortcut()
    {
        DeviceShortcuts.Add(NewDeviceShortcutRow(new DeviceShortcut()));
        SaveDeviceShortcuts();
    }

    private void RemoveDeviceShortcut(DeviceShortcutRow row)
    {
        DeviceShortcuts.Remove(row);
        SaveDeviceShortcuts();
    }

    private void SaveDeviceShortcuts()
    {
        // Written back from the rows rather than edited in place: normalising on a settings
        // replace builds a new list, which would leave a held reference editing a stale one.
        _settings.Current.DeviceShortcuts = DeviceShortcuts.Select(row => row.Model).ToList();
        _settings.Save();
        App.Current?.ApplyShortcuts();
        RefreshShortcutFailures();
    }

    private void OnSnapshotReloaded(object? sender, EventArgs e)
    {
        foreach (DeviceShortcutRow row in DeviceShortcuts)
        {
            row.RefreshNames();
        }

        RefreshShortcutFailures();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _homeAssistant.SnapshotReloaded -= OnSnapshotReloaded;
    }
}
