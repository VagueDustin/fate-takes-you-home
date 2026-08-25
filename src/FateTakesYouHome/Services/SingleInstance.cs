// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows.Interop;
using FateTakesYouHome.Interop;

namespace FateTakesYouHome.Services;

/// <summary>What a second launch is asking the running instance to do.</summary>
public enum ActivationIntent
{
    /// <summary>Open the full window. What a bare relaunch means.</summary>
    ShowMainWindow = 0,

    /// <summary>Open the tray panel. What <c>--panel</c> means.</summary>
    ShowPanel = 1,
}

/// <summary>
/// Ensures only one copy runs per user session, and lets a second launch wake the first.
/// </summary>
/// <remarks>
/// <para>
/// A tray app that can be started twice is a bug people notice immediately: two icons, two sets of
/// notifications, and settings that overwrite each other. The mutex is session-scoped rather than
/// global so that two users signed in at once each get their own instance, which is correct — the
/// settings and the DPAPI-protected token are per-user anyway.
/// </para>
/// <para>
/// The second instance broadcasts a registered window message before exiting. Registered messages
/// are globally unique by name, so this cannot collide with another application's.
/// </para>
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\VagueDustinEnterprises.FateTakesYouHome.Instance";

    private const string ActivationMessageName =
        "VagueDustinEnterprises.FateTakesYouHome.Activate";

    /// <summary>Broadcast target: every top-level window in the session.</summary>
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    private readonly Mutex _mutex;
    private HwndSource? _listener;
    private bool _disposed;

    private SingleInstance(Mutex mutex, bool isFirst)
    {
        _mutex = mutex;
        IsFirstInstance = isFirst;
    }

    /// <summary>The registered message id. Zero if registration failed.</summary>
    public static int ActivationMessage { get; } =
        NativeMethods.RegisterWindowMessage(ActivationMessageName);

    /// <summary>False when another copy is already running.</summary>
    public bool IsFirstInstance { get; }

    /// <summary>Raised when a second launch asks this instance to show something.</summary>
    public event EventHandler<ActivationIntent>? ActivationRequested;

    /// <summary>Claims the instance slot.</summary>
    public static SingleInstance Acquire()
    {
        // createdNew is false when the mutex already existed, i.e. another instance has it.
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

        if (createdNew)
        {
            return new SingleInstance(mutex, isFirst: true);
        }

        mutex.Dispose();

        // A disposed mutex still needs a placeholder so the caller can dispose uniformly.
        return new SingleInstance(new Mutex(initiallyOwned: false), isFirst: false);
    }

    /// <summary>
    /// Starts listening for activation requests from later launches.
    /// </summary>
    /// <remarks>
    /// Only meaningful on the first instance. The listener is a message-only window, so it costs
    /// nothing and never appears anywhere.
    /// </remarks>
    public void StartListening()
    {
        if (_listener is not null || !IsFirstInstance || ActivationMessage == 0)
        {
            return;
        }

        // A real (invisible) top-level window, not a message-only one. Message-only windows are
        // excluded from HWND_BROADCAST delivery, so a message-only listener compiles, runs, and
        // never hears a single activation — relaunching the app appears to do nothing at all.
        var parameters = new HwndSourceParameters("FateTakesYouHome.InstanceListener")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP: no frame, and never visible.
            ExtendedWindowStyle = 0x00000080,         // WS_EX_TOOLWINDOW: stays off the taskbar.
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
        };

        _listener = new HwndSource(parameters);
        _listener.AddHook(OnMessage);
    }

    /// <summary>
    /// Asks the running instance to show something. Called by a second launch before it exits.
    /// </summary>
    /// <remarks>
    /// The intent rides in wParam so that a shortcut with <c>--panel</c> opens the tray panel
    /// rather than the full window. Without it, every relaunch would mean the same thing, and
    /// binding a hotkey to the panel would be impossible once the app was already running.
    /// </remarks>
    public static void SignalExistingInstance(ActivationIntent intent)
    {
        if (ActivationMessage == 0)
        {
            return;
        }

        NativeMethods.PostMessage(
            HwndBroadcast, ActivationMessage, new IntPtr((int)intent), IntPtr.Zero);
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != ActivationMessage || ActivationMessage == 0)
        {
            return IntPtr.Zero;
        }

        handled = true;

        var intent = (ActivationIntent)(int)wParam;

        // An unrecognised value can only come from a future version of this app; opening the
        // window is the safe reading of "somebody launched me again".
        if (!Enum.IsDefined(intent))
        {
            intent = ActivationIntent.ShowMainWindow;
        }

        ActivationRequested?.Invoke(this, intent);
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_listener is not null)
        {
            _listener.RemoveHook(OnMessage);
            _listener.Dispose();
            _listener = null;
        }

        try
        {
            if (IsFirstInstance)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch (ApplicationException)
        {
            // Not the owning thread, which can happen during an abrupt shutdown.
        }

        _mutex.Dispose();
    }
}
