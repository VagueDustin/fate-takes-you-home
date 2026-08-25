// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Windows.Input;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.HomeAssistant.Models;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// The pure logic under the 0.3.0 platform features: update parsing, shortcut gestures,
/// widget clamping, and recorder-history parsing.
/// </summary>
public sealed class PlatformIntegrationTests
{
    // ------------------------------------------------------------------ updates

    [Theory]
    [InlineData("v0.3.0", "0.3.0")]
    [InlineData("0.3.0", "0.3.0")]
    [InlineData("V1.2", "1.2")]
    [InlineData("v1.0.0-beta.2", "1.0.0")]
    public void Version_tags_normalise(string tag, string expected) =>
        Assert.Equal(expected, UpdateService.NormalizeVersion(tag));

    [Fact]
    public void A_newer_release_with_an_msi_is_offered()
    {
        string json = """
        {
          "tag_name": "v9.9.9",
          "html_url": "https://github.com/VagueDustin/fate-takes-you-home/releases/tag/v9.9.9",
          "assets": [
            { "name": "FateTakesYouHome-9.9.9-portable-win-x64.zip", "size": 1, "browser_download_url": "https://example.invalid/zip" },
            { "name": "FateTakesYouHome-9.9.9-win-x64.msi", "size": 12345, "browser_download_url": "https://example.invalid/msi" }
          ]
        }
        """;

        UpdateInfo? update = UpdateService.TryParseLatest(json, new Version(0, 2, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(9, 9, 9), update.Version);
        Assert.EndsWith("/msi", update.InstallerUrl);
        Assert.Equal(12345, update.InstallerBytes);
    }

    [Fact]
    public void The_same_or_older_release_is_not_offered()
    {
        string json = """{ "tag_name": "v0.2.0", "assets": [] }""";

        Assert.Null(UpdateService.TryParseLatest(json, new Version(0, 2, 0)));
        Assert.Null(UpdateService.TryParseLatest(json, new Version(0, 3, 0)));
    }

    [Fact]
    public void A_release_without_an_installer_is_not_offered()
    {
        string json = """
        { "tag_name": "v9.9.9", "assets": [ { "name": "notes.txt", "size": 1, "browser_download_url": "https://example.invalid/x" } ] }
        """;

        Assert.Null(UpdateService.TryParseLatest(json, new Version(0, 2, 0)));
    }

    // ------------------------------------------------------------------ shortcuts

    [Theory]
    [InlineData("Ctrl+Alt+H", ModifierKeys.Control | ModifierKeys.Alt, Key.H)]
    [InlineData("ctrl + shift + 5", ModifierKeys.Control | ModifierKeys.Shift, Key.D5)]
    [InlineData("Win+F", ModifierKeys.Windows, Key.F)]
    [InlineData("F9", ModifierKeys.None, Key.F9)]
    public void Gestures_parse(string text, ModifierKeys modifiers, Key key)
    {
        HotkeyGesture? gesture = HotkeyGesture.Parse(text);

        Assert.NotNull(gesture);
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.Key);
    }

    [Theory]
    [InlineData("H")]
    [InlineData("Shift+H")]
    [InlineData("")]
    [InlineData("Ctrl+")]
    public void Gestures_that_would_eat_ordinary_typing_are_refused(string text) =>
        Assert.Null(HotkeyGesture.Parse(text));

    [Fact]
    public void Gestures_round_trip_through_their_text_form()
    {
        var gesture = new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Alt, Key.D7);

        Assert.Equal("Ctrl+Alt+7", gesture.ToString());
        Assert.Equal(gesture, HotkeyGesture.Parse(gesture.ToString()));
    }

    // ------------------------------------------------------------------ widgets

    [Fact]
    public void Widget_specs_are_clamped_into_the_grid()
    {
        var spec = new WidgetSpec { X = 10, Y = -2, W = 9, H = 99, Hours = 0 };

        spec.ClampTo(columns: 6);

        Assert.Equal(6, spec.W);
        Assert.Equal(0, spec.X);
        Assert.Equal(0, spec.Y);
        Assert.InRange(spec.H, 1, 6);
        Assert.InRange(spec.Hours, 1, 168);
    }

    // ------------------------------------------------------------------ history

    [Fact]
    public void Compressed_history_rows_parse_and_non_numbers_are_skipped()
    {
        string json = """
        {
          "sensor.temp": [
            { "state": "20.5", "last_updated": "2026-08-24T00:00:00+00:00" },
            { "s": "21.0", "lu": 1787875200.5 },
            { "s": "unavailable", "lu": 1787878800 },
            { "s": "21.5", "lu": 1787882400 }
          ]
        }
        """;

        using JsonDocument document = JsonDocument.Parse(json);

        IReadOnlyList<HaHistoryPoint> points =
            HaCommands.ParseNumericHistory(document.RootElement.Clone(), "sensor.temp");

        Assert.Equal(3, points.Count);
        Assert.Equal(20.5, points[0].Value);
        Assert.Equal(21.0, points[1].Value);
        Assert.Equal(21.5, points[2].Value);
        Assert.True(points[2].Time > points[1].Time);
    }

    [Fact]
    public void History_for_an_absent_entity_is_empty() =>
        Assert.Empty(HaCommands.ParseNumericHistory(null, "sensor.none"));
}
