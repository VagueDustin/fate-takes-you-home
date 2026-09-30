// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using FateTakesYouHome.Services;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// When the start-with-Windows entry is repointed at the running copy.
/// </summary>
/// <remarks>
/// The check runs on every launch and fails silently in both directions: too eager and a debug
/// build takes the entry from the installed app, too timid and an entry for a deleted copy stays
/// dead. Neither shows until the next sign-in.
/// </remarks>
public sealed class AutostartServiceTests
{
    private const string Installed =
        @"C:\Program Files\VagueDustin Enterprises\Fate Takes You Home\FateTakesYouHome.exe";

    private const string DevBuild =
        @"C:\Source\fate\src\FateTakesYouHome\bin\Debug\net8.0-windows\win-x64\FateTakesYouHome.exe";

    /// <summary>Stands in for the disk: only these executables exist.</summary>
    private static Func<string, bool> OnDisk(params string[] executables) =>
        path => executables.Contains(path, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Built with the method <c>SetEnabled</c> writes with, so a change to that format which the
    /// repair cannot read back fails here, rather than the repair quietly never firing again.
    /// </summary>
    [Fact]
    public void AnEntryWhoseExecutableIsGoneIsRepaired()
    {
        string registered = AutostartService.CommandLineFor(Installed);

        Assert.True(AutostartService.ShouldRepair(registered, DevBuild, OnDisk(DevBuild)));
    }

    /// <summary>
    /// The case this used to get wrong: a debug build started while the installed app was closed
    /// took the installed app's entry, and the installed app stopped starting with Windows.
    /// </summary>
    [Fact]
    public void AnEntryForAnotherCopyThatStillExistsIsLeftAlone()
    {
        string registered = AutostartService.CommandLineFor(Installed);

        Assert.False(
            AutostartService.ShouldRepair(registered, DevBuild, OnDisk(Installed, DevBuild)));
    }

    /// <summary>
    /// Nothing is on disk, so this holds because the path matched rather than because the
    /// executable was found.
    /// </summary>
    [Fact]
    public void AnEntryForThisCopyIsLeftAlone()
    {
        string registered = AutostartService.CommandLineFor(Installed);

        Assert.False(AutostartService.ShouldRepair(registered, Installed, OnDisk()));
    }

    /// <summary>
    /// Nothing is on disk here either, so any of these read as a path would be judged missing and
    /// repaired.
    /// </summary>
    [Theory]
    [InlineData("")]                                                     // Empty
    [InlineData(@"C:\Tools\FateTakesYouHome.exe --tray")]                // Not quoted
    [InlineData(@"""C:\Tools\FateTakesYouHome.exe --tray")]              // Quote never closed
    [InlineData(@""""" --tray")]                                         // Quotes around nothing
    [InlineData(@"""FateTakesYouHome.exe"" --tray")]                     // Relative
    [InlineData(@"""%LOCALAPPDATA%\Fate\FateTakesYouHome.exe"" --tray")] // Environment variable
    public void AnEntryThatDoesNotClearlyNameAnExecutableIsLeftAlone(string registered) =>
        Assert.False(AutostartService.ShouldRepair(registered, DevBuild, OnDisk()));
}
