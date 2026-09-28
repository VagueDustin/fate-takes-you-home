// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows.Input;
using System.Windows.Interop;
using FateTakesYouHome.Interop;

namespace FateTakesYouHome.Services;

/// <summary>The things a system-wide shortcut can be attached to.</summary>
public enum HotkeyAction
{
    /// <summary>Open (or close) the tray panel.</summary>
    OpenPanel,

    /// <summary>Open the full window.</summary>
    OpenWindow,

    /// <summary>Turn every light off.</summary>
    AllLightsOff,

    /// <summary>Run whichever pin is marked as the default action.</summary>
    RunDefaultPin,
}

/// <summary>A parsed keyboard gesture: modifiers plus one key.</summary>
public sealed record HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    /// <summary>Renders the gesture the way the settings page shows it, e.g. "Ctrl+Alt+H".</summary>
    public override string ToString()
    {
        var parts = new List<string>(4);

        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    /// <summary>Parses "Ctrl+Alt+H" style text. Returns null for anything unusable.</summary>
    public static HotkeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        ModifierKeys modifiers = ModifierKeys.None;
        Key key = Key.None;

        foreach (string rawPart in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (rawPart.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "WIN" or "WINDOWS":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    key = ParseKey(rawPart);
                    break;
            }
        }

        // A bare letter with no modifier would eat ordinary typing system-wide. Refuse it.
        bool hasRealModifier = (modifiers & ~ModifierKeys.Shift) != 0;
        bool functionKey = key is >= Key.F1 and <= Key.F24;

        if (key == Key.None || (!hasRealModifier && !functionKey))
        {
            return null;
        }

        return new HotkeyGesture(modifiers, key);
    }

    private static Key ParseKey(string name)
    {
        // Digits arrive as "0".."9" but the enum calls them D0..D9.
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            name = "D" + name;
        }

        return Enum.TryParse(name, ignoreCase: true, out Key parsed) ? parsed : Key.None;
    }

    private static string KeyName(Key key) =>
        key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString();
}

/// <summary>
/// One shortcut to register.
/// </summary>
/// <param name="Key">
/// Stable identity, echoed back by <see cref="HotkeyService.Pressed"/>. A
/// <see cref="HotkeyAction"/> name, or a device shortcut's key.
/// </param>
/// <param name="Gesture">"Ctrl+Alt+H" text, or null when unset.</param>
/// <param name="Name">What the shortcut does, for the message when two of them collide.</param>
public sealed record HotkeyRegistration(string Key, string? Gesture, string Name);

/// <summary>
/// Registers system-wide shortcuts and routes them to their actions.
/// </summary>
/// <remarks>
/// <para>
/// <c>RegisterHotKey</c> is the supported API for this: no keyboard hook, no polling, and Windows
/// itself refuses a combination another application already owns, which is surfaced per shortcut
/// so the settings page can say "taken" instead of silently not working.
/// </para>
/// <para>
/// Everything registers against a message-only window. Unlike broadcasts, <c>WM_HOTKEY</c> is
/// posted directly to the registering window, so message-only is fine here, and invisible even
/// to tooling that enumerates windows.
/// </para>
/// </remarks>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int FirstId = 0x4A7E;

    private readonly AppLog _log;
    private readonly Dictionary<int, string> _registered = [];
    private readonly Dictionary<string, (int Id, HotkeyGesture Gesture)> _held = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failures = new(StringComparer.Ordinal);
    private HwndSource? _window;
    private bool _disposed;

    public HotkeyService(AppLog log)
    {
        _log = log;
    }

    /// <summary>
    /// Raised on the UI thread when a registered shortcut is pressed, with the
    /// <see cref="HotkeyRegistration.Key"/> it was registered under.
    /// </summary>
    public event EventHandler<string>? Pressed;

    /// <summary>Why the shortcut registered under a key could not be, if it could not.</summary>
    public string? FailureFor(string key) =>
        _failures.TryGetValue(key, out string? reason) ? reason : null;

    /// <summary>
    /// Makes the registered shortcuts match the given list. Call whenever the settings change.
    /// </summary>
    /// <remarks>
    /// Only what differs is touched. This used to drop every registration and make them all again,
    /// and it runs on each keystroke of a device shortcut's name: every system-wide key blinked
    /// out and back per letter, long enough to lose a press, or for another application to take a
    /// combination in the gap.
    /// </remarks>
    public void Apply(IReadOnlyList<HotkeyRegistration> shortcuts)
    {
        EnsureWindow();

        IReadOnlyDictionary<string, string> conflicts = FindConflicts(shortcuts);
        var failedBefore = new HashSet<string>(_failures.Keys, StringComparer.Ordinal);
        var wanted = new Dictionary<string, HotkeyGesture>(StringComparer.Ordinal);

        _failures.Clear();

        foreach (HotkeyRegistration shortcut in shortcuts)
        {
            if (HotkeyGesture.Parse(shortcut.Gesture) is not { } gesture)
            {
                continue;
            }

            // Windows would refuse the second registration too, but its only complaint is
            // "in use", which would send somebody looking for another application to blame.
            if (conflicts.TryGetValue(shortcut.Key, out string? conflict))
            {
                _failures[shortcut.Key] = conflict;
                continue;
            }

            wanted[shortcut.Key] = gesture;
        }

        (IReadOnlyList<string> release, IReadOnlyList<string> claim) = PlanChanges(
            _held.ToDictionary(pair => pair.Key, pair => pair.Value.Gesture, StringComparer.Ordinal),
            wanted);

        // Releases first, so a combination moving from one shortcut to another is free to take.
        foreach (string key in release)
        {
            (int id, _) = _held[key];
            NativeMethods.UnregisterHotKey(_window!.Handle, id);
            _registered.Remove(id);
            _held.Remove(key);
        }

        foreach (string key in claim)
        {
            HotkeyGesture gesture = wanted[key];
            int id = NextFreeId();

            if (NativeMethods.RegisterHotKey(
                    _window!.Handle, id, ToNativeModifiers(gesture.Modifiers),
                    (uint)KeyInterop.VirtualKeyFromKey(gesture.Key)))
            {
                _registered[id] = key;
                _held[key] = (id, gesture);
            }
            else
            {
                // Almost always: another app owns the combination. It is tried again on every
                // apply, since that app may have let go, but only logged the first time.
                _failures[key] = $"{gesture} is already in use by another application.";

                if (!failedBefore.Contains(key))
                {
                    _log.Warning($"Could not register the shortcut {gesture} for {key}.");
                }
            }
        }
    }

    /// <summary>
    /// Which registrations to release and which to make, to go from what is held to what is wanted.
    /// </summary>
    /// <returns>
    /// Keys to release: no longer wanted, or wanted on a different combination. Keys to claim:
    /// wanted and not held on that combination, which includes any that failed last time.
    /// </returns>
    public static (IReadOnlyList<string> Release, IReadOnlyList<string> Claim) PlanChanges(
        IReadOnlyDictionary<string, HotkeyGesture> held,
        IReadOnlyDictionary<string, HotkeyGesture> wanted)
    {
        var release = held
            .Where(pair => !wanted.TryGetValue(pair.Key, out HotkeyGesture? gesture) || gesture != pair.Value)
            .Select(pair => pair.Key)
            .ToList();

        var claim = wanted
            .Where(pair => !held.TryGetValue(pair.Key, out HotkeyGesture? gesture) || gesture != pair.Value)
            .Select(pair => pair.Key)
            .ToList();

        return (release, claim);
    }

    private int NextFreeId()
    {
        int id = FirstId;

        while (_registered.ContainsKey(id))
        {
            id++;
        }

        return id;
    }

    /// <summary>
    /// Finds shortcuts that share a combination with an earlier one in the list.
    /// </summary>
    /// <returns>
    /// A message for each losing shortcut's key, naming the one that keeps the combination. The
    /// first in the list wins, which puts the application's own shortcuts ahead of device ones.
    /// </returns>
    public static IReadOnlyDictionary<string, string> FindConflicts(IEnumerable<HotkeyRegistration> shortcuts)
    {
        var owners = new Dictionary<HotkeyGesture, HotkeyRegistration>();
        var conflicts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (HotkeyRegistration shortcut in shortcuts)
        {
            // Compared as parsed gestures, so "ctrl+alt+l" and "Ctrl+Alt+L" are the same keys.
            if (HotkeyGesture.Parse(shortcut.Gesture) is not { } gesture)
            {
                continue;
            }

            if (owners.TryGetValue(gesture, out HotkeyRegistration? owner))
            {
                conflicts[shortcut.Key] = $"{gesture} is already the shortcut for {owner.Name}.";
            }
            else
            {
                owners[gesture] = shortcut;
            }
        }

        return conflicts;
    }

    /// <summary>How an application shortcut is named on the settings page and in messages.</summary>
    public static string Describe(HotkeyAction action) => action switch
    {
        HotkeyAction.OpenPanel => "Open the tray panel",
        HotkeyAction.OpenWindow => "Open the full window",
        HotkeyAction.AllLightsOff => "Turn off all lights",
        HotkeyAction.RunDefaultPin => "Run the default pin",
        _ => action.ToString(),
    };

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("FateTakesYouHome.Hotkeys")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };

        _window = new HwndSource(parameters);
        _window.AddHook(OnMessage);
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _registered.TryGetValue((int)wParam, out string? key))
        {
            handled = true;
            Pressed?.Invoke(this, key);
        }

        return IntPtr.Zero;
    }

    private void UnregisterAll()
    {
        if (_window is null)
        {
            return;
        }

        foreach (int id in _registered.Keys)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        }

        _registered.Clear();
        _held.Clear();
        _failures.Clear();
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint native = 0x4000; // MOD_NOREPEAT: holding the combination fires once.

        if (modifiers.HasFlag(ModifierKeys.Alt)) native |= 0x0001;
        if (modifiers.HasFlag(ModifierKeys.Control)) native |= 0x0002;
        if (modifiers.HasFlag(ModifierKeys.Shift)) native |= 0x0004;
        if (modifiers.HasFlag(ModifierKeys.Windows)) native |= 0x0008;

        return native;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        UnregisterAll();
        _window?.Dispose();
        _window = null;
    }
}
