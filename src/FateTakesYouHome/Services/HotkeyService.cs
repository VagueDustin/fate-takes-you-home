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
/// Registers system-wide shortcuts and routes them to their actions.
/// </summary>
/// <remarks>
/// <para>
/// <c>RegisterHotKey</c> is the supported API for this: no keyboard hook, no polling, and Windows
/// itself refuses a combination another application already owns — which is surfaced per shortcut
/// so the settings page can say "taken" instead of silently not working.
/// </para>
/// <para>
/// Everything registers against a message-only window. Unlike broadcasts, <c>WM_HOTKEY</c> is
/// posted directly to the registering window, so message-only is fine here — and invisible even
/// to tooling that enumerates windows.
/// </para>
/// </remarks>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private readonly AppLog _log;
    private readonly Dictionary<int, HotkeyAction> _registered = [];
    private readonly Dictionary<HotkeyAction, string> _failures = [];
    private HwndSource? _window;
    private bool _disposed;

    public HotkeyService(AppLog log)
    {
        _log = log;
    }

    /// <summary>Raised on the UI thread when a registered shortcut is pressed.</summary>
    public event EventHandler<HotkeyAction>? Pressed;

    /// <summary>Why a given action's shortcut could not be registered, if it could not.</summary>
    public string? FailureFor(HotkeyAction action) =>
        _failures.TryGetValue(action, out string? reason) ? reason : null;

    /// <summary>
    /// Drops every registration and applies the given map. Call whenever the settings change.
    /// </summary>
    public void Apply(IReadOnlyDictionary<HotkeyAction, string?> shortcuts)
    {
        EnsureWindow();
        UnregisterAll();

        foreach ((HotkeyAction action, string? text) in shortcuts)
        {
            if (HotkeyGesture.Parse(text) is not { } gesture)
            {
                continue;
            }

            int id = 0x4A7E + (int)action;
            uint modifiers = ToNativeModifiers(gesture.Modifiers);
            uint key = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);

            if (NativeMethods.RegisterHotKey(_window!.Handle, id, modifiers, key))
            {
                _registered[id] = action;
            }
            else
            {
                // Almost always: another app owns the combination.
                _failures[action] = $"{gesture} is already in use by another application.";
                _log.Warning($"Could not register the shortcut {gesture} for {action}.");
            }
        }
    }

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
        if (msg == WM_HOTKEY && _registered.TryGetValue((int)wParam, out HotkeyAction action))
        {
            handled = true;
            Pressed?.Invoke(this, action);
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
