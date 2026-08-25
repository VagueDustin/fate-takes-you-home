// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace FateTakesYouHome.Services;

/// <summary>
/// Manages the "start with Windows" registration.
/// </summary>
/// <remarks>
/// <para>
/// Uses the per-user <c>Run</c> key rather than a scheduled task or an all-users key. That choice
/// is deliberate: the Run key needs no elevation, so the setting can be toggled from inside the
/// app instead of requiring an admin prompt, and it shows up in Task Manager's Startup tab where
/// people expect to find it and can turn it off themselves.
/// </para>
/// <para>
/// Windows also records its own enabled/disabled state for Run entries in
/// <c>StartupApproved\Run</c>. If somebody disables the app there, the registry value still exists
/// but Windows ignores it — so <see cref="IsEnabled"/> checks both.
/// </para>
/// </remarks>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ApprovedKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>Passed on relaunch so the app knows to stay in the tray instead of opening a window.</summary>
    public const string TrayArgument = "--tray";

    private readonly AppLog _log;

    public AutostartService(AppLog log) => _log = log;

    /// <summary>The value name written under the Run key.</summary>
    public static string ValueName => AppPaths.ApplicationKey;

    /// <summary>Whether the app is registered to start with Windows and not disabled by the shell.</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKeyPath);

                if (run?.GetValue(ValueName) is not string)
                {
                    return false;
                }

                return !IsDisabledByShell();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                _log.Warning("Could not read the autostart registration.", ex);
                return false;
            }
        }
    }

    /// <summary>Adds or removes the registration.</summary>
    /// <returns>True when the registry now matches what was asked for.</returns>
    public bool SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Could not open the Run key.");

            if (!enabled)
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
                _log.Info("Removed the start-with-Windows registration.");
                return true;
            }

            string? executable = GetExecutablePath();

            if (executable is null)
            {
                _log.Error("Could not determine this application's path, so autostart was not enabled.");
                return false;
            }

            // Quoted because the install path contains spaces, and passed the tray flag so a
            // login launch does not throw a window in the user's face.
            run.SetValue(ValueName, $"\"{executable}\" {TrayArgument}", RegistryValueKind.String);

            if (IsDisabledByShell())
            {
                _log.Warning(
                    "The autostart entry was written, but Windows has this app disabled in the "
                    + "Startup apps list. It must be re-enabled from Task Manager or Settings.");
                return false;
            }

            _log.Info($"Registered to start with Windows: {executable}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.Error("Could not change the autostart registration.", ex);
            return false;
        }
    }

    /// <summary>
    /// Rewrites the registration when the executable has moved.
    /// </summary>
    /// <remarks>
    /// Running a portable copy after installing properly — or the reverse — otherwise leaves the
    /// Run key pointing at a path that no longer exists, and the app silently stops starting.
    /// </remarks>
    public void RepairIfStale()
    {
        try
        {
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);

            if (run?.GetValue(ValueName) is not string existing)
            {
                return;
            }

            string? executable = GetExecutablePath();
            if (executable is null)
            {
                return;
            }

            if (existing.Contains(executable, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            run.SetValue(ValueName, $"\"{executable}\" {TrayArgument}", RegistryValueKind.String);
            _log.Info($"Autostart pointed at a different copy of the app; updated it to {executable}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.Warning("Could not check whether the autostart registration was stale.", ex);
        }
    }

    /// <summary>
    /// True when Windows has switched this entry off in the Startup apps list.
    /// </summary>
    /// <remarks>
    /// The value is a binary blob whose first byte carries the state: even values are enabled,
    /// odd values are disabled. Everything after it is a timestamp we do not care about.
    /// </remarks>
    private bool IsDisabledByShell()
    {
        try
        {
            using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);

            if (approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state)
            {
                return false;
            }

            return (state[0] & 1) == 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// The path to launch on login.
    /// </summary>
    /// <remarks>
    /// <see cref="Environment.ProcessPath"/> is correct for a normal published build. The fallback
    /// covers being launched through the host (<c>dotnet run</c>), where the process path is
    /// <c>dotnet.exe</c> and registering it would be useless.
    /// </remarks>
    private static string? GetExecutablePath()
    {
        string? path = Environment.ProcessPath;

        if (path is not null
            && !Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string candidate = Path.Combine(AppPaths.InstallDirectory, "FateTakesYouHome.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>Opens the Windows Startup apps settings page.</summary>
    public void OpenWindowsStartupSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warning("Could not open the Windows startup settings page.", ex);
        }
    }
}
