// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.HomeAssistant.Models;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;

namespace FateTakesYouHome.ViewModels;

/// <summary>A pinned item, as shown in the reorderable list on the settings page.</summary>
public sealed partial class PinnedRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private bool _isDefaultAction;

    public required string EntityId { get; init; }

    /// <summary>The name Home Assistant reports, shown when no override is set.</summary>
    public required string ServerName { get; init; }

    public required bool IsMissing { get; init; }

    public string Placeholder => ServerName;
}

/// <summary>
/// Connection, behaviour, startup, and the pin list.
/// </summary>
/// <remarks>
/// The access token is never held in a bindable property. The view writes it straight through to
/// <see cref="SetAccessToken"/>, which encrypts it before it touches anything persistent — a
/// bindable string would linger in managed memory and would show up in any diagnostic that dumps
/// the view model.
/// </remarks>
public sealed partial class SettingsPageViewModel : ObservableObject, IDisposable
{
    private readonly AppLog _log;
    private readonly SettingsService _settings;
    private readonly HomeAssistantService _homeAssistant;
    private readonly TrayController _tray;

    private bool _loading;
    private bool _disposed;

    // -- Connection ------------------------------------------------------------------------------

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private bool _allowInvalidCertificate;

    [ObservableProperty]
    private string _tokenStatus = "No token stored";

    [ObservableProperty]
    private bool _hasPendingToken;

    [ObservableProperty]
    private string? _testResult;

    [ObservableProperty]
    private bool _testSucceeded;

    [ObservableProperty]
    private bool _isTesting;

    // -- Behaviour -------------------------------------------------------------------------------

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private string? _startupWarning;

    [ObservableProperty]
    private TrayAction _doubleClickAction;

    [ObservableProperty]
    private TrayAction _middleClickAction;

    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _pinFlyoutOpen;

    [ObservableProperty]
    private bool _disableAnimations;

    [ObservableProperty]
    private bool _verboseLogging;

    [ObservableProperty]
    private string? _statusMessage;

    private string? _pendingToken;

    public SettingsPageViewModel(
        AppLog log,
        SettingsService settings,
        HomeAssistantService homeAssistant,
        TrayController tray,
        UpdateService updates)
    {
        _log = log;
        Updates = updates;
        _settings = settings;
        _homeAssistant = homeAssistant;
        _tray = tray;

        _settings.Changed += OnSettingsChanged;
        _homeAssistant.SnapshotReloaded += OnSnapshotReloaded;

        LoadFromSettings();
    }

    /// <summary>The update checker, for the updates card.</summary>
    public UpdateService Updates { get; }

    /// <summary>Whether the daily release check runs at all.</summary>
    public bool CheckForUpdates
    {
        get => _settings.Current.CheckForUpdates;
        set
        {
            if (_settings.Current.CheckForUpdates == value)
            {
                return;
            }

            _settings.Current.CheckForUpdates = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public ObservableCollection<PinnedRowViewModel> Pins { get; } = [];

    public IReadOnlyList<TrayAction> TrayActions { get; } = Enum.GetValues<TrayAction>();

    // -- Log viewer --------------------------------------------------------------------------

    /// <summary>Formatted lines from the in-memory log tail, newest last.</summary>
    /// <remarks>
    /// Fed from <see cref="AppLog.Tail"/> rather than the file, so it keeps working when the file
    /// cannot be written — which is precisely the situation it exists to make visible.
    /// </remarks>
    public ObservableCollection<string> LogLines { get; } = [];

    /// <summary>One line saying whether log entries are reaching the disk.</summary>
    public string LogHealth =>
        _log.FileWriteError is { } error
            ? $"The log file cannot be written ({error}). The entries below are kept in memory only."
            : "Log entries are reaching the file normally.";

    public bool LogFileFailing => _log.FileWriteError is not null;

    [ObservableProperty]
    private bool _isLogViewerOpen;

    partial void OnIsLogViewerOpenChanged(bool value)
    {
        if (value)
        {
            RefreshLog();

            if (_logRefresh is null)
            {
                _logRefresh = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1),
                };
                _logRefresh.Tick += OnLogRefreshTick;
            }

            _logRefresh.Start();
        }
        else
        {
            _logRefresh?.Stop();
        }
    }

    private System.Windows.Threading.DispatcherTimer? _logRefresh;
    private int _logLinesSeen;
    private long _logNewestSeen;

    private void OnLogRefreshTick(object? sender, EventArgs e) => RefreshLog();

    private void RefreshLog()
    {
        IReadOnlyList<LogEntry> tail = _log.Tail();

        // Rebuilding a few hundred strings once a second, only while the viewer is open, is
        // cheaper than diffing — and it stays visibly live while verbose logging streams. The
        // ring buffer's count plateaus once full, so the newest timestamp is the change signal.
        long newest = tail.Count > 0 ? tail[^1].Timestamp.UtcTicks : 0;

        if (tail.Count == _logLinesSeen && newest == _logNewestSeen && LogLines.Count > 0)
        {
            return;
        }

        _logLinesSeen = tail.Count;
        _logNewestSeen = newest;

        LogLines.Clear();
        foreach (LogEntry entry in tail)
        {
            LogLines.Add(entry.Format());
        }

        OnPropertyChanged(nameof(LogHealth));
        OnPropertyChanged(nameof(LogFileFailing));
    }

    [RelayCommand]
    private void CopyLog()
    {
        try
        {
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, _log.Tail().Select(e => e.Format())));
            StatusMessage = "Recent log copied to the clipboard.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusMessage = "The clipboard is in use by another application; try again.";
        }
    }

    public bool HasPins => Pins.Count > 0;

    public static string SettingsFilePath => AppPaths.SettingsFile;

    public static string ThemesFolderPath => AppPaths.UserThemes;

    public static string LogFolderPath => AppPaths.LogsDirectory;

    public static string InstallPath => AppPaths.InstallDirectory;

    /// <summary>True when running from Program Files, which changes where things can be written.</summary>
    public static bool IsInstalled => AppPaths.IsInstalledSystemWide();

    // ------------------------------------------------------------------ loading

    private void LoadFromSettings()
    {
        _loading = true;

        AppSettings s = _settings.Current;

        ServerUrl = s.ServerUrl ?? string.Empty;
        AllowInvalidCertificate = s.AllowInvalidCertificate;
        TokenStatus = SecretProtector.Describe(_settings.GetAccessToken());

        DoubleClickAction = s.TrayDoubleClickAction;
        MiddleClickAction = s.TrayMiddleClickAction;
        CloseToTray = s.CloseToTray;
        PinFlyoutOpen = s.PinFlyoutOpen;
        DisableAnimations = s.DisableAnimations;
        VerboseLogging = s.VerboseLogging;

        StartWithWindows = App.Current?.Autostart.IsEnabled ?? s.StartWithWindows;

        RebuildPins();

        _loading = false;
    }

    private void RebuildPins()
    {
        foreach (PinnedRowViewModel row in Pins)
        {
            row.PropertyChanged -= OnPinRowChanged;
        }

        Pins.Clear();

        foreach (PinnedEntity pin in _settings.Current.Pinned)
        {
            HaEntityState? state = _homeAssistant.Find(pin.EntityId);

            var row = new PinnedRowViewModel
            {
                EntityId = pin.EntityId,
                ServerName = state?.FriendlyName ?? pin.EntityId,
                IsMissing = state is null,
                Label = pin.Label ?? string.Empty,
                IsDefaultAction = pin.IsDefaultAction,
            };

            row.PropertyChanged += OnPinRowChanged;
            Pins.Add(row);
        }

        OnPropertyChanged(nameof(HasPins));
    }

    // ------------------------------------------------------------------ the token

    /// <summary>
    /// Accepts a token from the view without it ever becoming a bindable property.
    /// </summary>
    /// <remarks>
    /// Held in a private field until Save, so that typing a token and then navigating away
    /// without saving does not silently change the stored credential.
    /// </remarks>
    public void SetAccessToken(string? token)
    {
        _pendingToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        HasPendingToken = _pendingToken is not null;

        TokenStatus = _pendingToken is not null
            ? "New token entered — not saved yet"
            : SecretProtector.Describe(_settings.GetAccessToken());

        TestResult = null;
    }

    [RelayCommand]
    private void ClearToken()
    {
        _pendingToken = null;
        HasPendingToken = false;

        _settings.SetAccessToken(null);
        TokenStatus = SecretProtector.Describe(null);
        StatusMessage = "Access token cleared.";
    }

    // ------------------------------------------------------------------ connection

    /// <summary>
    /// Tries the entered details without committing them.
    /// </summary>
    /// <remarks>
    /// Uses a throwaway client rather than the live one, so a bad address typed halfway through
    /// does not knock out the working connection the user is still relying on.
    /// </remarks>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        string? token = _pendingToken ?? _settings.GetAccessToken();

        if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(token))
        {
            TestSucceeded = false;
            TestResult = "Enter both a server address and an access token first.";
            return;
        }

        var options = new HaConnectionOptions
        {
            BaseUrl = ServerUrl.Trim(),
            AccessToken = token,
            AllowInvalidCertificate = AllowInvalidCertificate,
            CommandTimeout = TimeSpan.FromSeconds(12),
        };

        IReadOnlyList<string> problems = options.Validate();

        if (problems.Count > 0)
        {
            TestSucceeded = false;
            TestResult = string.Join(" ", problems);
            return;
        }

        IsTesting = true;
        TestResult = "Connecting…";

        var probe = new HaClient(options, _log.AsCallback(LogLevel.Debug));
        var finished = new TaskCompletionSource<(bool Ok, string Message)>();

        void OnState(object? sender, HaConnectionStateChangedEventArgs e)
        {
            if (e.State == HaConnectionState.Connected)
            {
                finished.TrySetResult((true, "Connected."));
            }
            else if (e.State == HaConnectionState.Failed)
            {
                finished.TrySetResult((false, e.Detail ?? "Could not connect."));
            }
        }

        probe.ConnectionStateChanged += OnState;

        try
        {
            probe.Start();

            Task timeout = Task.Delay(TimeSpan.FromSeconds(15));
            Task winner = await Task.WhenAny(finished.Task, timeout).ConfigureAwait(true);

            if (winner == timeout)
            {
                TestSucceeded = false;
                TestResult = "Timed out. Check the address, and that this PC can reach it.";
                return;
            }

            (bool ok, string message) = await finished.Task.ConfigureAwait(true);

            if (!ok)
            {
                TestSucceeded = false;
                TestResult = message;
                return;
            }

            // Connected. Say something concrete rather than just "OK".
            HaUser? user = await probe.GetCurrentUserAsync().ConfigureAwait(true);
            HaConfig? config = await probe.GetConfigAsync().ConfigureAwait(true);
            IReadOnlyList<HaEntityState> states = await probe.GetStatesAsync().ConfigureAwait(true);

            TestSucceeded = true;
            TestResult =
                $"Connected to {config?.LocationName ?? "Home Assistant"} "
                + $"{config?.Version} as {user?.Name ?? "unknown user"}. "
                + $"{states.Count} entities available.";
        }
        catch (Exception ex)
        {
            TestSucceeded = false;
            TestResult = ex.Message;
            _log.Warning("Connection test failed.", ex);
        }
        finally
        {
            probe.ConnectionStateChanged -= OnState;
            await probe.DisposeAsync().ConfigureAwait(true);
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task SaveConnectionAsync()
    {
        _settings.Current.ServerUrl = ServerUrl.Trim();
        _settings.Current.AllowInvalidCertificate = AllowInvalidCertificate;

        if (_pendingToken is not null)
        {
            _settings.SetAccessToken(_pendingToken);
            _pendingToken = null;
            HasPendingToken = false;
        }

        _settings.SaveNow();
        TokenStatus = SecretProtector.Describe(_settings.GetAccessToken());

        StatusMessage = "Saved. Reconnecting…";

        if (App.Current is { } app)
        {
            await app.ConnectAsync().ConfigureAwait(true);
        }

        StatusMessage = "Saved.";
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        StatusMessage = "Reconnecting…";
        await _homeAssistant.ReconnectAsync().ConfigureAwait(true);
        StatusMessage = null;
    }

    // ------------------------------------------------------------------ pins

    [RelayCommand]
    private void MovePinUp(PinnedRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        int index = Pins.IndexOf(row);

        if (index <= 0)
        {
            return;
        }

        Pins.Move(index, index - 1);
        CommitPins();
    }

    [RelayCommand]
    private void MovePinDown(PinnedRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        int index = Pins.IndexOf(row);

        if (index < 0 || index >= Pins.Count - 1)
        {
            return;
        }

        Pins.Move(index, index + 1);
        CommitPins();
    }

    [RelayCommand]
    private void RemovePin(PinnedRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.PropertyChanged -= OnPinRowChanged;
        Pins.Remove(row);
        CommitPins();
    }

    private void OnPinRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loading || sender is not PinnedRowViewModel changed)
        {
            return;
        }

        // Only one pin may be the default action, or the tray gesture is ambiguous.
        if (e.PropertyName == nameof(PinnedRowViewModel.IsDefaultAction) && changed.IsDefaultAction)
        {
            foreach (PinnedRowViewModel other in Pins.Where(p => !ReferenceEquals(p, changed)))
            {
                other.IsDefaultAction = false;
            }
        }

        CommitPins();
    }

    private void CommitPins()
    {
        if (_loading)
        {
            return;
        }

        _settings.Current.Pinned = Pins
            .Select(row => new PinnedEntity
            {
                EntityId = row.EntityId,
                Label = string.IsNullOrWhiteSpace(row.Label) ? null : row.Label.Trim(),
                IsDefaultAction = row.IsDefaultAction,
            })
            .ToList();

        _settings.Replace(_settings.Current);
        OnPropertyChanged(nameof(HasPins));
    }

    // ------------------------------------------------------------------ folders

    [RelayCommand]
    private void OpenLogFolder() => OpenPath(AppPaths.LogsDirectory);

    [RelayCommand]
    private void OpenSettingsFolder() => OpenPath(AppPaths.RoamingData);

    [RelayCommand]
    private void OpenThemesFolder() => OpenPath(AppPaths.UserThemes);

    [RelayCommand]
    private void OpenStartupSettings() => App.Current?.Autostart.OpenWindowsStartupSettings();

    private void OpenPath(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not open '{path}'.", ex);
            StatusMessage = "Could not open that folder.";
        }
    }

    // ------------------------------------------------------------------ reactions

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _settings.Current.StartWithWindows = value;
        _settings.Save();

        bool applied = App.Current?.Autostart.SetEnabled(value) ?? false;

        StartupWarning = value && !applied
            ? "Windows has this app switched off in the Startup apps list. Turn it back on there."
            : null;
    }

    partial void OnDoubleClickActionChanged(TrayAction value) =>
        Persist(s => s.TrayDoubleClickAction = value);

    partial void OnMiddleClickActionChanged(TrayAction value) =>
        Persist(s => s.TrayMiddleClickAction = value);

    partial void OnCloseToTrayChanged(bool value) => Persist(s => s.CloseToTray = value);

    partial void OnPinFlyoutOpenChanged(bool value) => Persist(s => s.PinFlyoutOpen = value);

    partial void OnDisableAnimationsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _settings.Current.DisableAnimations = value;
        _settings.Save();

        // The motion override lives in the theme pipeline, so the theme has to be rebuilt.
        App.Current?.Themes.Refresh();
    }

    partial void OnVerboseLoggingChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _settings.Current.VerboseLogging = value;
        _settings.Save();

        _log.MinimumLevel = value ? LogLevel.Debug : LogLevel.Info;
    }

    private void Persist(Action<AppSettings> edit)
    {
        if (_loading)
        {
            return;
        }

        edit(_settings.Current);
        _settings.Save();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        RebuildPins();
        _loading = false;
    }

    private void OnSnapshotReloaded(object? sender, EventArgs e)
    {
        _loading = true;
        RebuildPins();
        _loading = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _settings.Changed -= OnSettingsChanged;
        _homeAssistant.SnapshotReloaded -= OnSnapshotReloaded;

        if (_logRefresh is not null)
        {
            _logRefresh.Stop();
            _logRefresh.Tick -= OnLogRefreshTick;
        }

        foreach (PinnedRowViewModel row in Pins)
        {
            row.PropertyChanged -= OnPinRowChanged;
        }
    }
}
