using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.Services;
using FateTakesYouHome.Views;

namespace FateTakesYouHome;

/// <summary>
/// The composition root.
/// </summary>
/// <remarks>
/// <para>
/// The application deliberately has no main window at startup. It is a tray app: the shell is the
/// notification icon, and windows are created on demand and thrown away again. That is why
/// <c>ShutdownMode</c> is <c>OnExplicitShutdown</c> — the default would quit the moment the last
/// window closed.
/// </para>
/// <para>
/// Services are constructed by hand rather than through a container. There are nine of them with a
/// strictly linear dependency order, and reading that order in one place is more useful than the
/// indirection a container would add.
/// </para>
/// </remarks>
public partial class App : Application
{
    private SingleInstance? _instance;
    private AppLog? _log;
    private SettingsService? _settings;
    private ThemeService? _themes;
    private HomeAssistantService? _homeAssistant;
    private UpdateService? _updates;
    private HotkeyService? _hotkeys;
    private AutostartService? _autostart;
    private TrayController? _tray;
    private bool _shuttingDown;
    private bool _crashNoticeShown;

    /// <summary>The running application, typed. Null during startup and after shutdown.</summary>
    public static new App? Current => Application.Current as App;

    public AppLog Log => _log ?? throw new InvalidOperationException("Startup has not run yet.");

    public SettingsService Settings =>
        _settings ?? throw new InvalidOperationException("Startup has not run yet.");

    public ThemeService Themes =>
        _themes ?? throw new InvalidOperationException("Startup has not run yet.");

    public HomeAssistantService HomeAssistant =>
        _homeAssistant ?? throw new InvalidOperationException("Startup has not run yet.");

    public AutostartService Autostart =>
        _autostart ?? throw new InvalidOperationException("Startup has not run yet.");

    public TrayController Tray =>
        _tray ?? throw new InvalidOperationException("Startup has not run yet.");

    /// <summary>The product version, as shown in the about and help pages.</summary>
    public static string DisplayVersion { get; } =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0]
        ?? "0.0.0";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Refuse to be the second copy. Two tray icons controlling one house is nobody's idea of
        // a feature, and the two would fight over the settings file.
        bool wantsPanel = HasArgument(e.Args, PanelArgument);

        _instance = SingleInstance.Acquire();

        if (!_instance.IsFirstInstance)
        {
            // Hand the intent to the copy that is already running, then get out of its way.
            SingleInstance.SignalExistingInstance(
                wantsPanel ? ActivationIntent.ShowPanel : ActivationIntent.ShowMainWindow);

            Shutdown(0);
            return;
        }

        AppPaths.EnsureCreated();

        _log = new AppLog();
        _log.Info($"Fate Takes You Home {DisplayVersion} starting.");
        _log.Info($"Install directory: {AppPaths.InstallDirectory}");

        // Unhandled exceptions must be logged before the process dies, or a crash report is a
        // shrug. The dispatcher handler also keeps a UI-thread fault from killing the tray icon.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _settings = new SettingsService(_log);
        _log.MinimumLevel = _settings.Current.VerboseLogging ? LogLevel.Debug : LogLevel.Info;

        // Tell the theming library where this assembly keeps its embedded typefaces. The library
        // deliberately does not know the name of the application that hosts it.
        Theming.Rendering.ThemeResourceBuilder.EmbeddedFontBaseUri =
            new Uri("pack://application:,,,/FateTakesYouHome;component/Assets/Fonts/");

        _themes = new ThemeService(_log, _settings, Resources, Dispatcher);
        _themes.Initialise();

        _autostart = new AutostartService(_log);
        _autostart.RepairIfStale();

        _homeAssistant = new HomeAssistantService(_log, Dispatcher);

        _updates = new UpdateService(_log, _settings);
        _updates.ExitRequested += (_, _) => RequestShutdown();
        _updates.Start();

        _hotkeys = new HotkeyService(_log);
        _hotkeys.Pressed += OnHotkeyPressed;
        ApplyShortcuts();
        _settings.Changed += (_, _) => ApplyShortcuts();

        _tray = new TrayController(_log, _settings, _themes, _homeAssistant, _updates);
        _tray.Start();

        _instance.ActivationRequested += OnActivationRequested;
        _instance.StartListening();

        _ = ConnectAsync();

        bool launchedByWindows = HasArgument(e.Args, AutostartService.TrayArgument);

        // A configured connection is onboarding, completed — whoever set up the server and token
        // does not need the welcome tour re-offered on every launch because they skipped a
        // "finish" click they were never told mattered.
        if (!_settings.Current.HasCompletedOnboarding && _settings.Current.IsConfigured)
        {
            _settings.Current.HasCompletedOnboarding = true;
        }

        if (wantsPanel)
        {
            _tray.ShowFlyout();
        }
        else if (!launchedByWindows)
        {
            // Someone started the app by hand, so show them the app: the welcome on a first
            // run, the dashboard after that. Only the autostart's --tray goes straight to the
            // notification area, because at sign-in an unrequested window is an intrusion.
            _tray.ShowMainWindow(
                _settings.Current.HasCompletedOnboarding
                    ? MainWindowSection.Dashboard
                    : MainWindowSection.Welcome);
        }

        _settings.Current.LastRunVersion = DisplayVersion;
        _settings.Save();
    }

    /// <summary>The system-wide shortcuts, for the settings page to re-apply and interrogate.</summary>
    public HotkeyService Hotkeys =>
        _hotkeys ?? throw new InvalidOperationException("Startup has not run yet.");

    /// <summary>Reads the shortcut map out of settings and registers it.</summary>
    public void ApplyShortcuts()
    {
        if (_hotkeys is null || _settings is null)
        {
            return;
        }

        var map = new Dictionary<HotkeyAction, string?>();

        foreach (HotkeyAction action in Enum.GetValues<HotkeyAction>())
        {
            map[action] = _settings.Current.Shortcuts.TryGetValue(action.ToString(), out string? text)
                ? text
                : null;
        }

        _hotkeys.Apply(map);
    }

    private void OnHotkeyPressed(object? sender, HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.OpenPanel:
                _tray?.ToggleFlyout();
                break;

            case HotkeyAction.OpenWindow:
                _tray?.ShowMainWindow();
                break;

            case HotkeyAction.AllLightsOff:
                _ = _homeAssistant?.TurnOffAllLightsAsync();
                break;

            case HotkeyAction.RunDefaultPin:
                _ = _tray?.RunDefaultActionAsync();
                break;
        }
    }

    /// <summary>Applies the stored connection settings, or leaves the app idle when unconfigured.</summary>
    public async Task ConnectAsync()
    {
        HaConnectionOptions? options = Settings.BuildConnectionOptions();

        if (options is null)
        {
            Log.Info("No server address or token stored yet; staying disconnected.");
        }

        await HomeAssistant.ApplyConnectionAsync(options).ConfigureAwait(true);
    }

    private void OnActivationRequested(object? sender, ActivationIntent intent)
    {
        // A second launch means somebody clicked a shortcut expecting something to happen.
        Dispatcher.BeginInvoke(() =>
        {
            if (intent == ActivationIntent.ShowPanel)
            {
                _tray?.ShowFlyout();
            }
            else
            {
                _tray?.ShowMainWindow();
            }
        });
    }

    /// <summary>
    /// Opens the tray panel instead of the full window.
    /// </summary>
    /// <remarks>
    /// Exists so a shortcut — and therefore a Windows hotkey — can summon the panel directly.
    /// Works whether or not the app is already running: a second launch hands the intent to the
    /// first and exits.
    /// </remarks>
    public const string PanelArgument = "--panel";

    private static bool HasArgument(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ failure handling

    private void OnDispatcherUnhandledException(
        object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("Unhandled exception on the UI thread.", e.Exception);

        // Keep running. A fault while painting one tile should not take the tray icon with it,
        // and the log has the detail if this turns out to be worse than it looked.
        e.Handled = true;

        ShowCrashNotice(e.Exception);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Nothing can be recovered here; the runtime is already tearing down. Log and get out.
        if (e.ExceptionObject is Exception ex)
        {
            _log?.Error("Unhandled exception; the process is terminating.", ex);
        }

        _log?.Dispose();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.Warning("A background task faulted with nobody watching.", e.Exception);

        // Observing it prevents the exception from being rethrown on the finaliser thread.
        e.SetObserved();
    }

    /// <summary>
    /// Offers the log once per session after a UI-thread fault.
    /// </summary>
    /// <remarks>
    /// Rate-limited to one dialog. A fault during layout repeats on every render pass, and an
    /// unbounded stream of modal dialogs would be worse than the original bug.
    /// </remarks>
    private void ShowCrashNotice(Exception exception)
    {
        if (_shuttingDown || _log is null || _crashNoticeShown)
        {
            return;
        }

        _crashNoticeShown = true;

        MessageBoxResult result = MessageBox.Show(
            "Something went wrong and the action was abandoned. The application is still running.\n\n"
            + $"{exception.Message}\n\n"
            + "Open the log file?",
            "Fate Takes You Home",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_log.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warning("Could not open the log file.", ex);
        }
    }

    // ------------------------------------------------------------------ shutdown

    /// <summary>Exits the application, saving state first.</summary>
    public void RequestShutdown()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        Log.Info("Shutting down.");

        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;

        // Order matters: stop the UI surface first so nothing tries to render against a service
        // that is already gone, then close the connection, then flush state to disk.
        _tray?.Dispose();
        _instance?.Dispose();

        // The connection teardown is async; block briefly rather than leaving a socket open.
        // Two seconds is generous for a local close handshake and short enough that a hung
        // server cannot stop the process exiting.
        if (_homeAssistant is not null)
        {
            Task disconnect = _homeAssistant.DisposeAsync().AsTask();

            if (!disconnect.Wait(TimeSpan.FromSeconds(2)))
            {
                _log?.Warning("Timed out closing the Home Assistant connection; exiting anyway.");
            }
        }

        _themes?.Dispose();

        _settings?.SaveNow();
        _settings?.Dispose();

        _log?.Info("Goodbye.");
        _log?.Dispose();

        base.OnExit(e);
    }
}
