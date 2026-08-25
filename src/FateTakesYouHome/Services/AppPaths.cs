// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;

namespace FateTakesYouHome.Services;

/// <summary>
/// Every path the application reads or writes.
/// </summary>
/// <remarks>
/// Nothing is written inside the install directory. The application runs as the invoking user with
/// no elevation, and <c>C:\Program Files</c> is not writable by a standard user — an app that tries
/// either silently fails or, worse, gets redirected by UAC virtualisation into a shadow folder the
/// user can never find.
/// </remarks>
public static class AppPaths
{
    public const string CompanyName = "VagueDustin Enterprises";

    public const string ProductName = "Fate Takes You Home";

    /// <summary>Identifier used for the autostart entry and the single-instance mutex.</summary>
    public const string ApplicationKey = "FateTakesYouHome";

    /// <summary>Roaming settings and themes: follows the user between machines on a domain.</summary>
    public static string RoamingData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        CompanyName,
        ProductName);

    /// <summary>Logs and caches: machine-specific, deliberately not roamed.</summary>
    public static string LocalData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CompanyName,
        ProductName);

    public static string SettingsFile => Path.Combine(RoamingData, "settings.json");

    /// <summary>Where the user's own themes live, and what the hot-reload watcher watches.</summary>
    public static string UserThemes => Path.Combine(RoamingData, "Themes");

    public static string LogsDirectory => Path.Combine(LocalData, "Logs");

    /// <summary>The directory the executable was launched from.</summary>
    public static string InstallDirectory { get; } = AppContext.BaseDirectory;

    /// <summary>Read-only themes shipped with the install.</summary>
    public static string BundledThemes => Path.Combine(InstallDirectory, "Themes");

    /// <summary>Creates the folders the application writes to. Safe to call more than once.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RoamingData);
        Directory.CreateDirectory(UserThemes);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>
    /// True when running from a directory the current user cannot write to.
    /// </summary>
    /// <remarks>
    /// Used by the diagnostics page to explain why a portable build behaves differently from an
    /// installed one, rather than leaving somebody to guess.
    /// </remarks>
    public static bool IsInstalledSystemWide()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return (programFiles.Length > 0
                && InstallDirectory.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
            || (programFilesX86.Length > 0
                && InstallDirectory.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase));
    }
}
