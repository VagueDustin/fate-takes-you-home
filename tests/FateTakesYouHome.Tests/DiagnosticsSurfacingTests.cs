// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.IO;
using FateTakesYouHome.Converters;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;
using FateTakesYouHome.ViewModels;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// The app must never fail silently: a settings file that cannot be written and a log that cannot
/// reach disk both have to surface as state the UI can show. These exist because exactly that
/// silence happened on a real install — the app ran for nineteen minutes writing nothing, and the
/// only symptom was a theme that quietly refused to stick.
/// </summary>
public sealed class DiagnosticsSurfacingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fate-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A straggling handle on a temp directory is not worth failing the run over.
        }
    }

    // ------------------------------------------------------------------ settings save failures

    [Fact]
    public void Settings_save_failure_is_surfaced_and_recovery_clears_it()
    {
        using var log = new AppLog(Path.Combine(_root, "logs"));

        // The settings path runs through "blocker", which is a file — so creating the
        // directory for the settings file fails with an IOException.
        string blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "in the way");
        string settingsPath = Path.Combine(blocker, "settings.json");

        using var settings = new SettingsService(log, settingsPath);

        int stateChanges = 0;
        settings.SaveStateChanged += (_, _) => stateChanges++;

        settings.SaveNow();

        Assert.NotNull(settings.LastSaveError);
        Assert.Equal(1, stateChanges);

        // A second failure is the same failure; the event must not spam.
        settings.SaveNow();
        Assert.Equal(1, stateChanges);

        // Clear the obstruction and the next save must land and say so.
        File.Delete(blocker);
        settings.SaveNow();

        Assert.Null(settings.LastSaveError);
        Assert.Equal(2, stateChanges);
        Assert.True(File.Exists(settingsPath));
    }

    [Fact]
    public void Settings_save_success_reports_no_error()
    {
        using var log = new AppLog(Path.Combine(_root, "logs"));
        using var settings = new SettingsService(log, Path.Combine(_root, "ok", "settings.json"));

        settings.SaveNow();

        Assert.Null(settings.LastSaveError);
    }

    // ------------------------------------------------------------------ log file failures

    [Fact]
    public void Log_write_failure_is_surfaced_and_recovery_clears_it()
    {
        string folder = Path.Combine(_root, "logs-blocked");
        Directory.CreateDirectory(folder);

        // A directory squatting on the log file's name makes every append fail.
        string squatter = Path.Combine(folder, "fate-takes-you-home.log");
        Directory.CreateDirectory(squatter);

        using var log = new AppLog(folder);

        log.Info("this cannot reach the file");
        Assert.True(WaitFor(() => log.FileWriteError is not null), "the failure was never surfaced");

        // The in-memory tail keeps working regardless: that is what the diagnostics page shows.
        Assert.Contains(log.Tail(), entry => entry.Message.Contains("cannot reach the file"));

        Directory.Delete(squatter);

        log.Info("this one lands");
        Assert.True(WaitFor(() => log.FileWriteError is null), "recovery was never surfaced");
    }

    private static bool WaitFor(Func<bool> condition)
    {
        // The log writes on a background thread; give it a moment, but fail fast when satisfied.
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return condition();
    }

    // ------------------------------------------------------------------ labels

    [Fact]
    public void Every_tray_action_has_words_rather_than_an_enum_name()
    {
        var converter = new TrayActionToLabelConverter();

        foreach (TrayAction action in Enum.GetValues<TrayAction>())
        {
            string label = (string)converter.Convert(action, typeof(string), null, CultureInfo.InvariantCulture);

            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(action.ToString(), label);
        }
    }

    [Fact]
    public void Every_grouping_has_words_rather_than_an_enum_name()
    {
        var converter = new GroupingToLabelConverter();

        foreach (EntityGrouping grouping in Enum.GetValues<EntityGrouping>())
        {
            string label = (string)converter.Convert(grouping, typeof(string), null, CultureInfo.InvariantCulture);

            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(grouping.ToString(), label);
        }
    }

    // ------------------------------------------------------------------ room summaries

    [Theory]
    [InlineData(0, 4, "Dark", false)]
    [InlineData(1, 4, "1 light on", true)]
    [InlineData(3, 4, "3 lights on", true)]
    public void Room_summaries_say_the_right_thing(int on, int total, string expected, bool lit)
    {
        var room = new RoomSummary("Study", on, total);

        Assert.Equal(expected, room.Detail);
        Assert.Equal(lit, room.IsLit);
    }
}
