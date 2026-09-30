// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using System.Text.Json;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// A value this version does not recognise, mistyped by hand or written by a newer version, used to
/// make the whole settings file unreadable. An unreadable file is set aside and the app starts over
/// from defaults, so one wrong word cost the server, the token, every pin and both layouts. These
/// load real files the way the app does, and check that only the wrong value is repaired.
/// </summary>
public sealed class SettingsFileRepairTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fate-tests-").FullName;
    private readonly AppLog _log;

    public SettingsFileRepairTests()
    {
        _log = new AppLog(Path.Combine(_root, "logs"));
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose()
    {
        _log.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A straggling handle on a temp directory is not worth failing the run over.
        }
    }

    // ------------------------------------------------------------------ unknown names

    /// <summary>
    /// The two gestures have different defaults, so an unknown action cannot simply become one
    /// fixed value.
    /// </summary>
    [Fact]
    public void AnUnknownTrayActionFallsBackToThatGesturesOwnDefault()
    {
        // One mistyped, one a newer version might have.
        using SettingsService settings = Load(SettingsFile("""
            "trayDoubleClickAction": "OpenMainWindw",
            "trayMiddleClickAction": "ToggleMute"
            """));

        AssertTheRestSurvived(settings.Current);
        Assert.Equal(TrayAction.OpenMainWindow, settings.Current.TrayDoubleClickAction);
        Assert.Equal(TrayAction.RunDefaultAction, settings.Current.TrayMiddleClickAction);
    }

    [Fact]
    public void AnUnknownGroupingFallsBackToArea()
    {
        // The page calls it "By room", which makes this the likeliest thing to type by hand.
        using SettingsService settings = Load(SettingsFile("""
            "grouping": "Room"
            """));

        AssertTheRestSurvived(settings.Current);
        Assert.Equal(EntityGrouping.Area, settings.Current.Grouping);
    }

    /// <summary>
    /// A widget this version cannot draw is left out rather than drawn as something else, and the
    /// widgets around it keep their places.
    /// </summary>
    [Fact]
    public void AWidgetOfAnUnknownKindIsDroppedAndTheRestOfItsLayoutKept()
    {
        using SettingsService settings = Load(SettingsFile("""
            "homeWidgets": [
              { "kind": "Activity", "x": 0, "y": 0, "w": 6, "h": 1 },
              { "kind": "Clock", "x": 0, "y": 1, "w": 2, "h": 1 },
              { "kind": "Tile", "entityId": "light.desk", "x": 2, "y": 1, "w": 4, "h": 1 }
            ],
            "flyoutWidgets": [
              { "kind": "Camera", "entityId": "camera.porch", "x": 0, "y": 0, "w": 2, "h": 2 },
              { "kind": "QuickAction", "action": "allLightsOff", "x": 0, "y": 2, "w": 2, "h": 1 }
            ]
            """));

        AssertTheRestSurvived(settings.Current);

        Assert.NotNull(settings.Current.HomeWidgets);
        Assert.Collection(
            settings.Current.HomeWidgets,
            activity => Assert.Equal(WidgetKind.Activity, activity.Kind),
            tile =>
            {
                Assert.Equal(WidgetKind.Tile, tile.Kind);
                Assert.Equal("light.desk", tile.EntityId);
                Assert.Equal((2, 1, 4, 1), (tile.X, tile.Y, tile.W, tile.H));
            });

        Assert.NotNull(settings.Current.FlyoutWidgets);
        WidgetSpec quickAction = Assert.Single(settings.Current.FlyoutWidgets);
        Assert.Equal(WidgetKind.QuickAction, quickAction.Kind);
        Assert.Equal(WidgetSpec.AllLightsOffAction, quickAction.Action);
        Assert.Equal((0, 2), (quickAction.X, quickAction.Y));
    }

    /// <summary>
    /// Nothing a hand-edited file holds can be drawn from a null, and left in the layout, drawing
    /// it would throw.
    /// </summary>
    [Fact]
    public void ANullWhereAWidgetShouldBeIsDroppedToo()
    {
        using SettingsService settings = Load(SettingsFile("""
            "homeWidgets": [ null, { "kind": "Activity", "w": 6 } ]
            """));

        AssertTheRestSurvived(settings.Current);
        Assert.NotNull(settings.Current.HomeWidgets);
        Assert.Equal(WidgetKind.Activity, Assert.Single(settings.Current.HomeWidgets).Kind);
    }

    // ------------------------------------------------------------------ values of the wrong shape

    /// <summary>
    /// Not every wrong value is a wrong name. A null broke the file as surely as a typo, a number
    /// no member has was kept and then did nothing, and an object or array has to be read past in
    /// full, or the reader loses its place and everything after it is lost too.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("\"\"")]
    [InlineData("3.5")]
    [InlineData("99")]
    [InlineData("""{ "name": "OpenMainWindow", "nested": [1, { "deeper": true }] }""")]
    [InlineData("""["Floor", "Area"]""")]
    public void AValueOfTheWrongShapeIsRepairedWithoutLosingWhatFollowsIt(string value)
    {
        using SettingsService settings = Load(SettingsFile($$"""
            "trayDoubleClickAction": {{value}},
            "grouping": {{value}},
            "homeWidgets": [
              { "kind": {{value}}, "entityId": "light.desk", "x": 0, "y": 0, "w": 3 },
              { "kind": "Rooms", "x": 0, "y": 1, "w": 6, "h": 2 }
            ]
            """));

        AssertTheRestSurvived(settings.Current);
        Assert.Equal(TrayAction.OpenMainWindow, settings.Current.TrayDoubleClickAction);
        Assert.Equal(EntityGrouping.Area, settings.Current.Grouping);
        Assert.NotNull(settings.Current.HomeWidgets);
        Assert.Equal(WidgetKind.Rooms, Assert.Single(settings.Current.HomeWidgets).Kind);
    }

    // ------------------------------------------------------------------ what still reads

    /// <summary>
    /// JsonStringEnumConverter matched a name whatever its case, and took a number in place of
    /// one. A file edited by hand may lean on either, and neither should now read as a mistake.
    /// </summary>
    [Fact]
    public void AKnownValueStillReadsWhateverItsCaseOrAsANumber()
    {
        using SettingsService settings = Load(SettingsFile("""
            "trayDoubleClickAction": "toggleflyout",
            "trayMiddleClickAction": 0,
            "grouping": "FLOOR",
            "homeWidgets": [ { "kind": "sparkline", "entityId": "sensor.office_temperature" } ]
            """));

        AssertTheRestSurvived(settings.Current);
        Assert.Equal(TrayAction.ToggleFlyout, settings.Current.TrayDoubleClickAction);
        Assert.Equal(TrayAction.None, settings.Current.TrayMiddleClickAction);
        Assert.Equal(EntityGrouping.Floor, settings.Current.Grouping);
        Assert.NotNull(settings.Current.HomeWidgets);
        Assert.Equal(WidgetKind.Sparkline, Assert.Single(settings.Current.HomeWidgets).Kind);
    }

    /// <summary>
    /// The next save writes what the repair left, by name, exactly as JsonStringEnumConverter
    /// wrote it, so a version from before the repair can still read the file afterwards.
    /// </summary>
    [Fact]
    public void WhatARepairLeavesIsSavedByName()
    {
        using SettingsService settings = Load(SettingsFile("""
            "trayMiddleClickAction": "ToggleMute",
            "grouping": "Room",
            "flyoutWidgets": [
              { "kind": "Camera", "entityId": "camera.porch" },
              { "kind": "Tile", "entityId": "light.desk", "y": 1 }
            ]
            """));

        settings.SaveNow();

        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        JsonElement root = saved.RootElement;

        Assert.Equal("OpenMainWindow", root.GetProperty("trayDoubleClickAction").GetString());
        Assert.Equal("RunDefaultAction", root.GetProperty("trayMiddleClickAction").GetString());
        Assert.Equal("Area", root.GetProperty("grouping").GetString());

        JsonElement tile = Assert.Single(root.GetProperty("flyoutWidgets").EnumerateArray());
        Assert.Equal("Tile", tile.GetProperty("kind").GetString());
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Puts <paramref name="json"/> where the settings file lives and loads it as the app does.</summary>
    private SettingsService Load(string json)
    {
        File.WriteAllText(SettingsPath, json);
        return new SettingsService(_log, SettingsPath);
    }

    /// <summary>
    /// A settings file holding what somebody would be sorry to lose, with the values under test in
    /// the middle, so whatever they do to the reader, what comes after them has to survive it.
    /// </summary>
    private static string SettingsFile(string values) => $$"""
        {
          "serverUrl": "http://homeassistant.local:8123",
          {{values}},
          "protectedToken": "stands-in-for-a-dpapi-blob",
          "themeId": "nord",
          "pinned": [
            { "entityId": "light.desk", "label": "Desk lamp", "isDefaultAction": true },
            { "entityId": "lock.front_door", "confirmBeforeRunning": true }
          ]
        }
        """;

    /// <summary>Checks that the file was read rather than set aside, and that all of it was kept.</summary>
    private void AssertTheRestSurvived(AppSettings settings)
    {
        Assert.False(File.Exists(SettingsPath + ".invalid"), "The settings file was set aside as unreadable.");
        Assert.Equal("http://homeassistant.local:8123", settings.ServerUrl);
        Assert.Equal("stands-in-for-a-dpapi-blob", settings.ProtectedToken);
        Assert.Equal("nord", settings.ThemeId);
        Assert.Collection(
            settings.Pinned,
            desk =>
            {
                Assert.Equal("light.desk", desk.EntityId);
                Assert.Equal("Desk lamp", desk.Label);
                Assert.True(desk.IsDefaultAction);
            },
            door =>
            {
                Assert.Equal("lock.front_door", door.EntityId);
                Assert.True(door.ConfirmBeforeRunning);
            });
    }
}
