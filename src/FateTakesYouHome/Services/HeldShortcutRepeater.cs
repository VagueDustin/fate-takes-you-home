// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FateTakesYouHome.Interop;

namespace FateTakesYouHome.Services;

/// <summary>
/// The timing of a held key: whether a stepping shortcut is due its next step.
/// </summary>
/// <remarks>
/// Kept apart from the timer and the keyboard so the rules can be tested with a clock that does
/// as it is told.
/// </remarks>
/// <param name="holdDelay">How long the keys stay down before a press counts as a hold.</param>
/// <param name="stepInterval">The time between steps once it does.</param>
public sealed class HoldRepeat(TimeSpan holdDelay, TimeSpan stepInterval)
{
    private DateTime _pressedAt;
    private DateTime _lastStepAt;

    /// <summary>True from a press until the keys are let go or the hold is stopped.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// The keys went down and the press took its step. Starts the hold over, so a second press
    /// before the first was noticed as let go waits out the delay again.
    /// </summary>
    public void Pressed(DateTime now)
    {
        _pressedAt = now;
        _lastStepAt = now;
        IsActive = true;
    }

    /// <summary>
    /// Whether to take another step now, recording it as taken if so. Letting go of the keys ends
    /// the hold for good; only a new press starts another.
    /// </summary>
    /// <param name="keysDown">Whether every key of the combination is still held.</param>
    /// <param name="stepInFlight">
    /// Whether the last step is still on its way. Never stepping over one means a slow connection
    /// slows the ramp down, rather than queueing steps that carry on after the keys are let go.
    /// </param>
    public bool ShouldStep(DateTime now, bool keysDown, bool stepInFlight)
    {
        if (!IsActive)
        {
            return false;
        }

        if (!keysDown)
        {
            IsActive = false;
            return false;
        }

        if (stepInFlight || now - _pressedAt < holdDelay || now - _lastStepAt < stepInterval)
        {
            return false;
        }

        _lastStepAt = now;
        return true;
    }

    /// <summary>Ends the hold, as a failed step does: stepping on into a failure only repeats it.</summary>
    public void Stop() => IsActive = false;
}

/// <summary>
/// Keeps a stepping device shortcut going while its keys are held, the way a volume or dimmer key
/// does.
/// </summary>
/// <remarks>
/// <para>
/// Windows reports a hotkey being pressed but never being let go, and passing the keyboard's own
/// repeat through would mean up to thirty presses a second: more than a light can follow, and far
/// more than anybody can stop at the level they wanted. So hotkeys stay registered without repeat,
/// and while a press is held this looks at the keys themselves and steps at a pace of its own.
/// </para>
/// <para>
/// How long a press must last to count as a hold is the keyboard repeat delay set in Windows, so
/// a held shortcut starts repeating when a held letter would.
/// </para>
/// </remarks>
public sealed class HeldShortcutRepeater : IDisposable
{
    /// <summary>
    /// The pace of a held key: four steps a second, so a 10% step crosses the whole range in two
    /// and a half seconds, slow enough to let go where you meant to.
    /// </summary>
    public static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>How often held keys are looked at: well inside the gap between steps.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(40);

    private readonly DispatcherTimer _timer;
    private HoldRepeat? _hold;
    private HotkeyGesture? _gesture;
    private Func<Task<bool>>? _step;
    private int _inFlight;
    private bool _disposed;

    public HeldShortcutRepeater()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = PollInterval };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// The combination was pressed: takes a step now, and another every <see cref="StepInterval"/>
    /// for as long as every key of it stays down.
    /// </summary>
    /// <param name="step">Performs one step. Returns false when it failed, which ends the hold.</param>
    public void Press(HotkeyGesture gesture, Func<Task<bool>> step)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        ArgumentNullException.ThrowIfNull(step);

        if (_disposed)
        {
            return;
        }

        // Keyboard delay runs from 0 (about a quarter of a second) to 3 (about a second).
        _hold = new HoldRepeat(
            TimeSpan.FromMilliseconds(250 * (SystemParameters.KeyboardDelay + 1)), StepInterval);
        _hold.Pressed(DateTime.UtcNow);
        _gesture = gesture;
        _step = step;

        _ = TakeStepAsync(step);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_hold is not { } hold || _gesture is not { } gesture || _step is not { } step)
        {
            Stop();
            return;
        }

        if (hold.ShouldStep(DateTime.UtcNow, AreDown(gesture), _inFlight > 0))
        {
            _ = TakeStepAsync(step);
        }
        else if (!hold.IsActive)
        {
            Stop();
        }
    }

    private async Task TakeStepAsync(Func<Task<bool>> step)
    {
        _inFlight++;
        bool succeeded;

        try
        {
            succeeded = await step().ConfigureAwait(true);
        }
        finally
        {
            _inFlight--;
        }

        // Only the hold this step belongs to: a newer press has a hold of its own.
        if (!succeeded && ReferenceEquals(step, _step))
        {
            _hold?.Stop();
        }
    }

    private void Stop()
    {
        _timer.Stop();
        _hold = null;
        _gesture = null;
        _step = null;
    }

    /// <summary>Whether every key of the combination is down right now.</summary>
    private static bool AreDown(HotkeyGesture gesture) =>
        IsDown(KeyInterop.VirtualKeyFromKey(gesture.Key))
        && (!gesture.Modifiers.HasFlag(ModifierKeys.Control) || IsDown(NativeMethods.VK_CONTROL))
        && (!gesture.Modifiers.HasFlag(ModifierKeys.Alt) || IsDown(NativeMethods.VK_MENU))
        && (!gesture.Modifiers.HasFlag(ModifierKeys.Shift) || IsDown(NativeMethods.VK_SHIFT))
        && (!gesture.Modifiers.HasFlag(ModifierKeys.Windows)
            || IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN));

    private static bool IsDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
