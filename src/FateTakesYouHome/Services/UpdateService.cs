// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FateTakesYouHome.Services;

/// <summary>What the update checker knows right now.</summary>
public enum UpdateStatus
{
    /// <summary>No check has happened yet.</summary>
    Unknown,

    /// <summary>A check is in flight.</summary>
    Checking,

    /// <summary>The installed version is the latest release.</summary>
    UpToDate,

    /// <summary>A newer release exists and its installer is known.</summary>
    UpdateAvailable,

    /// <summary>The installer is being downloaded.</summary>
    Downloading,

    /// <summary>The installer is on disk and ready to run.</summary>
    ReadyToInstall,

    /// <summary>The last check or download failed. Details in <see cref="Detail"/>.</summary>
    Failed,
}

/// <summary>A newer release, as parsed from the GitHub API.</summary>
public sealed record UpdateInfo(Version Version, string InstallerUrl, long InstallerBytes, string? ReleaseNotesUrl);

/// <summary>
/// Checks GitHub releases for a newer version, downloads the installer, and hands off to msiexec.
/// </summary>
/// <remarks>
/// <para>
/// The check is a single anonymous GET against the public releases API: no token, no telemetry,
/// nothing sent beyond the request itself. It runs shortly after startup and once a day after
/// that, and can be turned off in Settings. Nothing installs without the user clicking the
/// button: an app that replaces itself unasked is indistinguishable from malware to the person
/// whose tray it lives in.
/// </para>
/// <para>
/// The handoff is <c>msiexec /i package.msi /passive</c>, watched by a hidden PowerShell that
/// waits for it and then starts the app again; see <see cref="RelaunchScript"/>. The app exits
/// straight after the handoff so the installer can replace its files.
/// </para>
/// </remarks>
public sealed partial class UpdateService : ObservableObject, IDisposable
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/VagueDustin/fate-takes-you-home/releases/latest";

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly AppLog _log;
    private readonly SettingsService _settings;
    private readonly HttpClient _http;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    [ObservableProperty]
    private UpdateStatus _status = UpdateStatus.Unknown;

    /// <summary>One human sentence about the current state.</summary>
    [ObservableProperty]
    private string _detail = "Not checked yet.";

    /// <summary>The newer release, when one is known.</summary>
    [ObservableProperty]
    private UpdateInfo? _available;

    private string? _downloadedInstaller;

    public UpdateService(AppLog log, SettingsService settings)
    {
        _log = log;
        _settings = settings;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FateTakesYouHome", App.DisplayVersion));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.Timeout = TimeSpan.FromSeconds(30);

        _timer = new DispatcherTimer { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;

            if (_settings.Current.CheckForUpdates)
            {
                await CheckAsync(manual: false).ConfigureAwait(true);
            }
        };
    }

    /// <summary>Raised when the service wants the application to exit so the installer can run.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Starts the periodic check.</summary>
    public void Start() => _timer.Start();

    /// <summary>The version currently running, for display beside the check button.</summary>
    public static string CurrentVersion => App.DisplayVersion;

    /// <summary>The settings page's "check now" button.</summary>
    [RelayCommand]
    private Task CheckNowAsync() => CheckAsync(manual: true);

    /// <summary>Every release, with the notes for each, newest first.</summary>
    public const string ReleasesPageUrl = "https://github.com/VagueDustin/fate-takes-you-home/releases";

    /// <summary>
    /// Opens the notes for the newer release when one is known, otherwise every release's notes.
    /// </summary>
    [RelayCommand]
    private void OpenReleaseNotes()
    {
        // The URL came off the network, so only ever hand the shell a GitHub page.
        string url = Available?.ReleaseNotesUrl is { } notes
                     && notes.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
            ? notes
            : ReleasesPageUrl;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not open '{url}'.", ex);
        }
    }

    /// <summary>The banner's "install and restart" button.</summary>
    [RelayCommand]
    private Task InstallAsync() => DownloadAndInstallAsync();

    /// <summary>Checks GitHub for a newer release.</summary>
    public async Task CheckAsync(bool manual)
    {
        if (Status is UpdateStatus.Checking or UpdateStatus.Downloading)
        {
            return;
        }

        Status = UpdateStatus.Checking;
        Detail = "Checking for updates…";

        try
        {
            string json = await _http.GetStringAsync(LatestReleaseUrl).ConfigureAwait(true);

            UpdateInfo? latest = TryParseLatest(json, Version.Parse(NormalizeVersion(App.DisplayVersion)));

            if (latest is null)
            {
                Available = null;
                Status = UpdateStatus.UpToDate;
                Detail = $"Up to date. You are on {App.DisplayVersion}.";

                if (manual)
                {
                    _log.Info("Update check: already on the latest release.");
                }
            }
            else
            {
                Available = latest;
                Status = UpdateStatus.UpdateAvailable;
                Detail = $"Version {latest.Version} is available.";
                _log.Info($"Update check: {latest.Version} is available "
                          + $"({latest.InstallerBytes / (1024 * 1024)} MB).");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Status = UpdateStatus.Failed;
            Detail = "Could not reach GitHub to check for updates.";

            // Routine offline failures stay quiet unless the user explicitly asked.
            if (manual)
            {
                _log.Warning("Update check failed.", ex);
            }
        }
    }

    /// <summary>Downloads the available installer and, when told to, runs it.</summary>
    public async Task DownloadAndInstallAsync()
    {
        if (Available is not { } update)
        {
            return;
        }

        Status = UpdateStatus.Downloading;
        Detail = $"Downloading version {update.Version}…";

        try
        {
            string folder = Path.Combine(AppPaths.LocalData, "Updates");
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, $"FateTakesYouHome-{update.Version}-win-x64.msi");

            using (HttpResponseMessage response = await _http
                       .GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead)
                       .ConfigureAwait(true))
            {
                response.EnsureSuccessStatusCode();

                await using FileStream file = File.Create(target);
                await response.Content.CopyToAsync(file).ConfigureAwait(true);
            }

            long size = new FileInfo(target).Length;

            if (update.InstallerBytes > 0 && size != update.InstallerBytes)
            {
                File.Delete(target);
                throw new IOException(
                    $"The download was {size} bytes but the release says {update.InstallerBytes}.");
            }

            _downloadedInstaller = target;
            Status = UpdateStatus.ReadyToInstall;
            Detail = $"Version {update.Version} is ready to install.";
            _log.Info($"Update downloaded to {target}.");

            Install();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or UnauthorizedAccessException)
        {
            Status = UpdateStatus.Failed;
            Detail = "The download failed. Nothing was changed.";
            _log.Warning("Update download failed.", ex);
        }
    }

    /// <summary>Hands the downloaded installer to msiexec and asks the app to exit.</summary>
    private void Install()
    {
        if (_downloadedInstaller is not { } installer || !File.Exists(installer))
        {
            return;
        }

        _log.Info("Starting the installer and exiting.");

        Process? msiexec;

        try
        {
            // /passive shows only a progress bar. Elevation is the installer's own prompt to make.
            msiexec = Process.Start(new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{installer}\" /passive",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Policy can block msiexec, and security software can take the file between the
            // download and this line. Either way the app must not exit for an install that never
            // started, or go on offering one that cannot. The file is kept for a manual run.
            Status = UpdateStatus.Failed;
            Detail = $"Windows would not start the installer. It is saved at {installer}.";
            _log.Warning("Could not start the installer.", ex);
            return;
        }

        // Without a path to start again (dotnet run with nothing installed) the installer runs alone.
        if (msiexec is not null && AutostartService.GetExecutablePath() is { } app)
        {
            StartAfterInstaller(msiexec.Id, app, Path.GetDirectoryName(installer));
        }

        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The copy the MSI installs, wherever the running copy is.</summary>
    private static string InstalledExecutable => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "VagueDustin Enterprises",
        "Fate Takes You Home",
        "FateTakesYouHome.exe");

    /// <summary>Starts a hidden PowerShell that runs <see cref="RelaunchScript"/>.</summary>
    /// <remarks>
    /// <para>
    /// <c>/passive</c> skips the installer's UI sequence, so the exit dialog's "Start now" never
    /// runs, and the app exits before the installer has even asked for elevation. Something that
    /// outlives the app has to do the restart. A custom action in the MSI's execute sequence was
    /// the alternative, but that sequence runs in the elevated installer service, and a tray app
    /// started from there would run elevated, cut off from Explorer's messages.
    /// </para>
    /// <para>
    /// msiexec itself is still started by the app, not by the watcher, so a policy that blocks it
    /// is reported here rather than lost in a hidden console. A watcher that cannot start costs
    /// only the restart: the install goes ahead.
    /// </para>
    /// </remarks>
    private void StartAfterInstaller(int installerProcessId, string app, string? workingDirectory)
    {
        string script = RelaunchScript(installerProcessId, InstalledExecutable, app);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "
                            + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = false,
                CreateNoWindow = true,

                // Anywhere but the install folder, which the waiting process must not hold open.
                WorkingDirectory = workingDirectory,
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _log.Warning("Could not arrange a restart after the installer; start the app by hand.", ex);
        }
    }

    /// <summary>
    /// Waits for msiexec, then starts <paramref name="installed"/> if it exists and
    /// <paramref name="fallback"/> if not.
    /// </summary>
    /// <remarks>
    /// The app is started whatever msiexec returns. After an upgrade the installed copy is the new
    /// version, even when the update was run from a portable copy. After a refused UAC prompt or a
    /// rolled-back install it is the old version, or the portable copy when nothing is installed.
    /// Either way nobody is left with no app after pressing "Install and restart".
    /// </remarks>
    public static string RelaunchScript(int installerProcessId, string installed, string fallback) =>
        $"Wait-Process -Id {installerProcessId} -ErrorAction SilentlyContinue; "
        + $"$app = if (Test-Path -LiteralPath {Quote(installed)}) {{ {Quote(installed)} }} else {{ {Quote(fallback)} }}; "
        + "Start-Process -FilePath $app";

    /// <summary>A PowerShell single-quoted literal, which treats the typographic quotes as quotes too.</summary>
    private static string Quote(string value) =>
        "'" + Regex.Replace(value, "['‘’‚‛]", "$0$0") + "'";

    // ------------------------------------------------------------------ parsing

    /// <summary>
    /// Reads the GitHub "latest release" payload and returns the update it describes, or null
    /// when the release is not newer than <paramref name="current"/> or carries no installer.
    /// </summary>
    public static UpdateInfo? TryParseLatest(string json, Version current)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("tag_name", out JsonElement tagElement)
            || tagElement.GetString() is not { Length: > 0 } tag)
        {
            return null;
        }

        if (!Version.TryParse(NormalizeVersion(tag), out Version? released) || released <= current)
        {
            return null;
        }

        string? notesUrl = root.TryGetProperty("html_url", out JsonElement urlElement)
            ? urlElement.GetString()
            : null;

        if (!root.TryGetProperty("assets", out JsonElement assets)
            || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string? name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;

            if (name is null || !name.EndsWith("-win-x64.msi", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? url = asset.TryGetProperty("browser_download_url", out JsonElement u)
                ? u.GetString()
                : null;

            if (url is null)
            {
                continue;
            }

            long bytes = asset.TryGetProperty("size", out JsonElement sizeElement)
                ? sizeElement.GetInt64()
                : 0;

            return new UpdateInfo(released, url, bytes, notesUrl);
        }

        return null;
    }

    /// <summary>Strips a leading "v" and pads to a parseable version string.</summary>
    public static string NormalizeVersion(string tag)
    {
        string trimmed = tag.Trim().TrimStart('v', 'V');

        // "0.3" parses; "0.3.0-beta.1" needs the prerelease cut off.
        int dash = trimmed.IndexOf('-');
        return dash > 0 ? trimmed[..dash] : trimmed;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _http.Dispose();
    }
}
