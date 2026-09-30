// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Nodes;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.HomeAssistant.Models;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;
using FateTakesYouHome.ViewModels;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// Device shortcuts: what a press sends, which targets it skips, and how shortcuts are stored.
/// </summary>
public sealed class DeviceShortcutTests
{
    private const int MediaPause = HaFeatures.MediaPlayer.Pause;
    private const int MediaPowered = HaFeatures.MediaPlayer.TurnOn | HaFeatures.MediaPlayer.TurnOff;

    private static HaEntityState Entity(string entityId, string state, JsonObject? attributes = null)
    {
        var json = new JsonObject
        {
            ["entity_id"] = entityId,
            ["state"] = state,
            ["attributes"] = attributes ?? new JsonObject(),
        };

        return JsonSerializer.Deserialize<HaEntityState>(json.ToJsonString())!;
    }

    private static HaEntityState DimmableLight(string entityId, string state) =>
        Entity(entityId, state, new JsonObject { ["supported_color_modes"] = new JsonArray("brightness") });

    private static HaEntityState Player(string entityId, string state, int features, bool? muted = null)
    {
        var attributes = new JsonObject { ["supported_features"] = features };

        if (muted is not null)
        {
            attributes["is_volume_muted"] = muted;
        }

        return Entity(entityId, state, attributes);
    }

    // ------------------------------------------------------------------ groups

    /// <summary>
    /// The reason a group decides once: flipping each member would leave a half-on group half on
    /// forever, just with the halves swapped.
    /// </summary>
    [Fact]
    public void AGroupThatIsPartlyOnTogglesAllOff()
    {
        IReadOnlyList<HaServiceCall> calls = DeviceActions.Plan(
            DeviceAction.Toggle,
            [DimmableLight("light.desk", "on"), DimmableLight("light.shelf", "off")]);

        HaServiceCall call = Assert.Single(calls);
        Assert.Equal(("homeassistant", "turn_off"), (call.Domain, call.Service));
        Assert.Equal(new[] { "light.desk", "light.shelf" }, call.EntityIds);
    }

    [Fact]
    public void AGroupThatIsAllOffTogglesAllOn()
    {
        IReadOnlyList<HaServiceCall> calls = DeviceActions.Plan(
            DeviceAction.Toggle,
            [DimmableLight("light.desk", "off"), Entity("switch.fan_plug", "off")]);

        HaServiceCall call = Assert.Single(calls);
        Assert.Equal(("homeassistant", "turn_on"), (call.Domain, call.Service));
        Assert.Equal(2, call.EntityIds.Count);
    }

    [Fact]
    public void MixedDomainsSendOneCallPerService()
    {
        IReadOnlyList<HaServiceCall> calls = DeviceActions.Plan(
            DeviceAction.TurnOn,
            [
                DimmableLight("light.desk", "off"),
                Entity("cover.blind", "closed"),
                Entity("switch.fan_plug", "off"),
            ]);

        Assert.Equal(2, calls.Count);
        Assert.Equal(("homeassistant", "turn_on"), (calls[0].Domain, calls[0].Service));
        Assert.Equal(new[] { "light.desk", "switch.fan_plug" }, calls[0].EntityIds);
        Assert.Equal(("cover", "open_cover"), (calls[1].Domain, calls[1].Service));
    }

    [Fact]
    public void AShortcutNeverUnlocksADoor()
    {
        Assert.Empty(DeviceActions.Plan(DeviceAction.TurnOn, [Entity("lock.side_door", "locked")]));

        // All off, so the toggle goes on, and the lock sits it out.
        IReadOnlyList<HaServiceCall> on = DeviceActions.Plan(
            DeviceAction.Toggle,
            [DimmableLight("light.hall", "off"), Entity("lock.side_door", "locked")]);

        Assert.DoesNotContain(on, c => c.Service == "unlock");
    }

    [Fact]
    public void AShortcutCanLockADoorOnTheWayOut()
    {
        IReadOnlyList<HaServiceCall> calls = DeviceActions.Plan(
            DeviceAction.Toggle,
            [DimmableLight("light.hall", "on"), Entity("lock.side_door", "unlocked")]);

        Assert.Contains(calls, c => c is { Domain: "lock", Service: "lock" });
    }

    [Fact]
    public void AScenePlaysNoPartInDecidingWhichWayAGroupToggles()
    {
        // A scene's state is a timestamp, never "on". If it counted, it could not change anything;
        // if it counted as on, the group could never be switched on.
        IReadOnlyList<HaServiceCall> on = DeviceActions.Plan(
            DeviceAction.Toggle,
            [Entity("scene.reading", "2026-01-01T00:00:00+00:00"), DimmableLight("light.desk", "off")]);

        Assert.Contains(on, c => c is { Domain: "scene", Service: "turn_on" });

        IReadOnlyList<HaServiceCall> off = DeviceActions.Plan(
            DeviceAction.Toggle,
            [Entity("scene.reading", "2026-01-01T00:00:00+00:00"), DimmableLight("light.desk", "on")]);

        Assert.DoesNotContain(off, c => c.Domain == "scene");
    }

    [Fact]
    public void UnavailableTargetsAreLeftOut()
    {
        IReadOnlyList<HaServiceCall> calls = DeviceActions.Plan(
            DeviceAction.TurnOff,
            [DimmableLight("light.desk", "on"), DimmableLight("light.shelf", "unavailable")]);

        Assert.Equal(new[] { "light.desk" }, Assert.Single(calls).EntityIds);
    }

    /// <summary>
    /// A scene or button nobody has used yet reads "unknown", which used to count as unavailable
    /// and left a shortcut pointed at one doing nothing at all.
    /// </summary>
    [Fact]
    public void AShortcutFiresASceneThatHasNeverRun()
    {
        IReadOnlyList<HaServiceCall> calls = DeviceActions.Plan(
            DeviceAction.TurnOn,
            [Entity("scene.reading", "unknown"), Entity("button.doorbell_chime", "unknown")]);

        Assert.Equal(2, calls.Count);
        Assert.Contains(calls, c => c is { Domain: "scene", Service: "turn_on" });
        Assert.Contains(calls, c => c is { Domain: "button", Service: "press" });
    }

    [Fact]
    public void NothingIsSentWhenNoTargetCanTakeTheAction()
    {
        Assert.Empty(DeviceActions.Plan(DeviceAction.Pause, [DimmableLight("light.desk", "on")]));
        Assert.Empty(DeviceActions.Plan(DeviceAction.Toggle, []));
    }

    [Fact]
    public void ATargetListedTwiceIsSentOnce()
    {
        IReadOnlyList<HaServiceCall> calls = DeviceActions.Plan(
            DeviceAction.TurnOn,
            [DimmableLight("light.desk", "off"), DimmableLight("light.desk", "off")]);

        Assert.Single(Assert.Single(calls).EntityIds);
    }

    // ------------------------------------------------------------------ brightness

    /// <summary>
    /// A step lets Home Assistant do the arithmetic against the real brightness; an absolute level
    /// computed from the local cache would race it on a quick second press.
    /// </summary>
    [Fact]
    public void BrightnessStepsAreSentAsSteps()
    {
        HaServiceCall up = Assert.Single(
            DeviceActions.Plan(DeviceAction.BrightnessUp, [DimmableLight("light.desk", "on")], 15));
        HaServiceCall down = Assert.Single(
            DeviceActions.Plan(DeviceAction.BrightnessDown, [DimmableLight("light.desk", "on")]));

        Assert.Equal(("light", "turn_on"), (up.Domain, up.Service));
        Assert.Equal(15, up.Data!["brightness_step_pct"]);
        Assert.Equal(-10, down.Data!["brightness_step_pct"]);
    }

    [Fact]
    public void BrightnessSkipsThingsThatCannotDim()
    {
        IReadOnlyList<HaServiceCall> calls = DeviceActions.Plan(
            DeviceAction.BrightnessUp,
            [
                DimmableLight("light.desk", "on"),
                Entity("light.porch", "on", new JsonObject { ["supported_color_modes"] = new JsonArray("onoff") }),
                Entity("switch.fan_plug", "on"),
            ]);

        Assert.Equal(new[] { "light.desk" }, Assert.Single(calls).EntityIds);
    }

    [Fact]
    public void SettingBrightnessToZeroTurnsTheLightsOff()
    {
        HaServiceCall call = Assert.Single(
            DeviceActions.Plan(DeviceAction.SetBrightness, [DimmableLight("light.desk", "on")], 0));

        Assert.Equal(("light", "turn_off"), (call.Domain, call.Service));
    }

    [Theory]
    [InlineData(DeviceAction.BrightnessUp, 0, 1)]
    [InlineData(DeviceAction.BrightnessUp, 250, 100)]
    [InlineData(DeviceAction.SetBrightness, 0, 0)]
    [InlineData(DeviceAction.SetBrightness, -5, 0)]
    [InlineData(DeviceAction.SetBrightness, 42.5, 43)]
    public void PercentagesAreHeldToWhatTheActionCanUse(DeviceAction action, double given, int expected)
    {
        Assert.Equal(expected, DeviceActions.ClampValue(action, given));
    }

    // ------------------------------------------------------------------ media

    [Fact]
    public void MediaActionsSkipPlayersWithoutTheFeature()
    {
        IReadOnlyList<HaServiceCall> calls = DeviceActions.Plan(
            DeviceAction.Pause,
            [
                Player("media_player.speaker", "playing", MediaPause),
                Player("media_player.radio", "playing", HaFeatures.MediaPlayer.VolumeSet),
            ]);

        HaServiceCall call = Assert.Single(calls);
        Assert.Equal("media_pause", call.Service);
        Assert.Equal(new[] { "media_player.speaker" }, call.EntityIds);
    }

    [Fact]
    public void APausedPlayerIsStillOnAsFarAsAToggleIsConcerned()
    {
        HaServiceCall call = Assert.Single(DeviceActions.Plan(
            DeviceAction.Toggle,
            [Player("media_player.speaker", "paused", MediaPowered)]));

        Assert.Equal("turn_off", call.Service);
    }

    [Fact]
    public void MuteTogglesAsAGroup()
    {
        int features = HaFeatures.MediaPlayer.VolumeMute;

        HaServiceCall mute = Assert.Single(DeviceActions.Plan(
            DeviceAction.ToggleMute,
            [
                Player("media_player.speaker", "playing", features, muted: true),
                Player("media_player.radio", "playing", features, muted: false),
            ]));

        HaServiceCall unmute = Assert.Single(DeviceActions.Plan(
            DeviceAction.ToggleMute,
            [
                Player("media_player.speaker", "playing", features, muted: true),
                Player("media_player.radio", "playing", features, muted: true),
            ]));

        Assert.Equal(true, mute.Data!["is_volume_muted"]);
        Assert.Equal(false, unmute.Data!["is_volume_muted"]);
    }

    /// <summary>
    /// A thermostat that cannot be switched off is left out of a group's "off", instead of making
    /// Home Assistant refuse the whole batched call over it.
    /// </summary>
    [Fact]
    public void ThermostatsAndFansThatCannotSwitchAreLeftOut()
    {
        HaEntityState oldThermostat = Entity("climate.hall", "heat", new JsonObject { ["supported_features"] = 1 });
        HaEntityState newThermostat = Entity(
            "climate.study", "heat",
            new JsonObject { ["supported_features"] = HaFeatures.Climate.TurnOn | HaFeatures.Climate.TurnOff });
        HaEntityState fanOnlyOn = Entity("fan.desk", "on", new JsonObject { ["supported_features"] = HaFeatures.Fan.TurnOn });

        HaServiceCall call = Assert.Single(DeviceActions.Plan(
            DeviceAction.TurnOff,
            [DimmableLight("light.desk", "on"), oldThermostat, newThermostat, fanOnlyOn]));

        Assert.Equal(new[] { "light.desk", "climate.study" }, call.EntityIds);
        Assert.Empty(DeviceActions.Plan(DeviceAction.Toggle, [fanOnlyOn]));
    }

    // ------------------------------------------------------------------ hotkey clashes

    [Fact]
    public void TheSecondShortcutOnACombinationIsToldWhichOneHasIt()
    {
        IReadOnlyDictionary<string, string> conflicts = HotkeyService.FindConflicts(
        [
            new HotkeyRegistration("AllLightsOff", "Ctrl+Alt+L", "Turn off all lights"),
            new HotkeyRegistration("device:a", "Ctrl+Alt+L", "Toggle Desk lamp"),
        ]);

        Assert.False(conflicts.ContainsKey("AllLightsOff"));
        Assert.Equal("Ctrl+Alt+L is already the shortcut for Turn off all lights.", conflicts["device:a"]);
    }

    [Fact]
    public void CombinationsAreComparedAsKeysNotAsText()
    {
        IReadOnlyDictionary<string, string> conflicts = HotkeyService.FindConflicts(
        [
            new HotkeyRegistration("device:a", "Ctrl+Alt+L", "Toggle Desk lamp"),
            new HotkeyRegistration("device:b", "alt + ctrl + l", "Toggle Shelf"),
        ]);

        Assert.True(conflicts.ContainsKey("device:b"));
    }

    [Fact]
    public void ShortcutsWithoutKeysNeverClash()
    {
        IReadOnlyDictionary<string, string> conflicts = HotkeyService.FindConflicts(
        [
            new HotkeyRegistration("device:a", null, "Toggle Desk lamp"),
            new HotkeyRegistration("device:b", null, "Toggle Shelf"),
            new HotkeyRegistration("device:c", "not a shortcut", "Pause Speaker"),
        ]);

        Assert.Empty(conflicts);
    }

    // ------------------------------------------------------------------ storage

    [Fact]
    public void AHandEditedShortcutListIsRepairedOnLoad()
    {
        List<DeviceShortcut> repaired = DeviceShortcut.Normalise(
        [
            new DeviceShortcut { Id = "same", EntityIds = ["light.desk", " ", "LIGHT.DESK", "light.shelf"] },
            new DeviceShortcut { Id = "same", Action = (DeviceAction)999, Value = double.NaN },
            null!,
            new DeviceShortcut { Id = "", Action = DeviceAction.BrightnessUp, Value = 400 },
        ]);

        Assert.Equal(3, repaired.Count);
        Assert.Equal(3, repaired.Select(s => s.Id).Distinct().Count());
        Assert.Equal(new[] { "light.desk", "light.shelf" }, repaired[0].EntityIds);
        Assert.Equal(DeviceAction.Toggle, repaired[1].Action);
        Assert.Null(repaired[1].Value);
        Assert.Equal(100, repaired[2].Value);
    }

    /// <summary>A newly added row has no devices yet, and must survive a restart as it is.</summary>
    [Fact]
    public void AShortcutWithNoDevicesYetIsKept()
    {
        Assert.Single(DeviceShortcut.Normalise([new DeviceShortcut()]));
    }

    [Fact]
    public void ActionsAreStoredByNameSoTheFileStaysReadable()
    {
        var settings = new AppSettings
        {
            DeviceShortcuts =
            [
                new DeviceShortcut
                {
                    Gesture = "Ctrl+Alt+Up",
                    Action = DeviceAction.BrightnessUp,
                    EntityIds = ["light.desk"],
                    Value = 20,
                },
            ],
        };

        string json = JsonSerializer.Serialize(settings);
        Assert.Contains("\"action\":\"BrightnessUp\"", json);

        DeviceShortcut back = Assert.Single(JsonSerializer.Deserialize<AppSettings>(json)!.DeviceShortcuts);
        Assert.Equal(DeviceAction.BrightnessUp, back.Action);
        Assert.Equal(new[] { "light.desk" }, back.EntityIds);
        Assert.Equal(20, back.Value);
    }

    [Fact]
    public void ACloneDoesNotShareItsDeviceList()
    {
        var original = new DeviceShortcut { EntityIds = ["light.desk"] };
        DeviceShortcut copy = original.Clone();

        copy.EntityIds.Add("light.shelf");

        Assert.Single(original.EntityIds);
        Assert.Equal(original.Id, copy.Id);
    }

    [Fact]
    public void DescriptionsNameUpToTwoDevices()
    {
        static string Name(string id) => id == "light.desk" ? "Desk lamp" : "Shelf";

        Assert.Equal("Toggle Desk lamp", new DeviceShortcut { EntityIds = ["light.desk"] }.Describe(Name));
        Assert.Equal(
            "Turn off Desk lamp and Shelf",
            new DeviceShortcut { Action = DeviceAction.TurnOff, EntityIds = ["light.desk", "light.shelf"] }.Describe(Name));
        Assert.Equal(
            "Pause 3 devices",
            new DeviceShortcut { Action = DeviceAction.Pause, EntityIds = ["a.a", "b.b", "c.c"] }.Describe(Name));
    }
}

/// <summary>Planned calls sent through the real client to the fake server.</summary>
[Trait("Category", "Integration")]
public sealed class DeviceShortcutIntegrationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static async Task<HaClient> ConnectAsync(FakeHomeAssistantServer server)
    {
        var client = new HaClient(new HaConnectionOptions
        {
            BaseUrl = server.BaseUrl,
            AccessToken = server.ExpectedToken,
            CommandTimeout = TimeSpan.FromSeconds(5),
            PingInterval = TimeSpan.FromMilliseconds(300),
            ReconnectMinDelay = TimeSpan.FromMilliseconds(100),
            ReconnectMaxDelay = TimeSpan.FromSeconds(1),
        });

        client.Start();
        await FakeHomeAssistantServer.WaitUntilAsync(
            () => client.State == HaConnectionState.Connected, Patience);

        return client;
    }

    [Fact]
    public async Task AGroupGoesOutAsOneCallWithAListOfTargets()
    {
        await using var server = new FakeHomeAssistantServer();
        await using HaClient client = await ConnectAsync(server);

        await client.SendAsync([new HaServiceCall("homeassistant", "turn_off", ["light.desk", "light.shelf"])]);

        JsonNode call = await server.WaitForCommandAsync("call_service", Patience);
        JsonArray targets = call["target"]!["entity_id"]!.AsArray();

        Assert.Equal(
            new[] { "light.desk", "light.shelf" },
            targets.Select(t => t!.GetValue<string>()).ToArray());
    }

    /// <summary>
    /// In a mixed group, one domain turning a call down is no reason to leave the others alone,
    /// but the refusal must still reach the caller.
    /// </summary>
    [Fact]
    public async Task ARefusalDoesNotStopTheRestOfTheGroup()
    {
        await using var server = new FakeHomeAssistantServer
        {
            RefuseServiceCallsWith = "Entity does not support this service.",
        };
        await using HaClient client = await ConnectAsync(server);

        HaCommandException refused = await Assert.ThrowsAsync<HaCommandException>(() => client.SendAsync(
        [
            new HaServiceCall("media_player", "media_pause", ["media_player.speaker"]),
            new HaServiceCall("homeassistant", "turn_off", ["light.desk"]),
        ]));

        Assert.Equal("Entity does not support this service.", refused.ServerMessage);
        Assert.Equal(2, server.Received.Count(n => n["type"]?.GetValue<string>() == "call_service"));
    }
}

/// <summary>What the device picker offers, and in what order.</summary>
public sealed class DeviceShortcutSuggestionTests
{
    private static readonly DeviceShortcutCandidate[] House =
    [
        new("switch.garage_plug", "Garage plug", null),
        new("light.shelf_strip", "Shelf strip", "Study"),
        new("light.desk_lamp", "Desk lamp", "Study"),
        new("switch.kettle_plug", "Kettle plug", "Kitchen"),
        new("switch.power_strip", "Power strip", "Office"),
        new("switch.teapot_warmer", "Teapot warmer", "Office"),
    ];

    private static string[] Ids(IReadOnlyList<DeviceShortcutMatch> matches) =>
        matches.Select(m => m.EntityId).ToArray();

    /// <summary>A new shortcut's picker must have something in it before anything is typed.</summary>
    [Fact]
    public void WithNothingTypedEveryDeviceIsOfferedRoomByRoom()
    {
        Assert.Equal(
            new[]
            {
                "switch.kettle_plug",
                "switch.power_strip", "switch.teapot_warmer",
                "light.desk_lamp", "light.shelf_strip",
                "switch.garage_plug",
            },
            Ids(DeviceShortcutRow.Suggest(House, [], "", 50)));
    }

    [Fact]
    public void DevicesAlreadyChosenAreNotOfferedAgain()
    {
        Assert.DoesNotContain(
            "light.desk_lamp",
            Ids(DeviceShortcutRow.Suggest(House, ["LIGHT.DESK_LAMP"], "", 50)));
    }

    [Fact]
    public void NamesThatStartWithTheQueryComeFirst()
    {
        // "po" is inside "Teapot" too; the device whose name starts with it is the one meant.
        Assert.Equal(
            new[] { "switch.power_strip", "switch.teapot_warmer" },
            Ids(DeviceShortcutRow.Suggest(House, [], "po", 50)));
    }

    [Fact]
    public void TypingARoomFindsWhatIsInIt()
    {
        Assert.Equal(
            new[] { "light.desk_lamp", "light.shelf_strip" },
            Ids(DeviceShortcutRow.Suggest(House, [], "study", 50)));
    }

    [Fact]
    public void TheListIsCapped()
    {
        Assert.Equal(2, DeviceShortcutRow.Suggest(House, [], "  ", 2).Count);
    }

    [Fact]
    public void EachSuggestionSaysWhereItIs()
    {
        IReadOnlyList<DeviceShortcutMatch> matches = DeviceShortcutRow.Suggest(House, [], "", 50);

        Assert.Equal("Kitchen · switch.kettle_plug", matches[0].Detail);
        Assert.Equal("switch.garage_plug", matches[^1].Detail);
    }
}

/// <summary>Names people give their device shortcuts.</summary>
public sealed class DeviceShortcutNameTests
{
    private static string Name(string id) => id == "light.desk" ? "Desk lamp" : "Shelf";

    [Fact]
    public void ANamedShortcutIsCalledByItsName()
    {
        var shortcut = new DeviceShortcut { Label = "Office printers", EntityIds = ["light.desk"] };

        Assert.Equal("Office printers", shortcut.Describe(Name));
        Assert.Equal("Toggle Desk lamp", shortcut.DescribeAction(Name));
    }

    /// <summary>The clash message is where a name earns its keep: it is how the other shortcut is identified.</summary>
    [Fact]
    public void AClashNamesTheOtherShortcutByItsName()
    {
        var named = new DeviceShortcut { Label = "Office printers", Gesture = "Alt+1", EntityIds = ["light.desk"] };
        var other = new DeviceShortcut { Gesture = "Alt+1", EntityIds = ["light.shelf"] };

        IReadOnlyDictionary<string, string> conflicts = HotkeyService.FindConflicts(
        [
            new HotkeyRegistration(named.HotkeyKey, named.Gesture, named.Describe(Name)),
            new HotkeyRegistration(other.HotkeyKey, other.Gesture, other.Describe(Name)),
        ]);

        Assert.Equal("Alt+1 is already the shortcut for Office printers.", conflicts[other.HotkeyKey]);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Office printers ", "Office printers")]
    public void ABlankNameIsNoName(string? given, string? kept)
    {
        DeviceShortcut shortcut = Assert.Single(DeviceShortcut.Normalise([new DeviceShortcut { Label = given }]));

        Assert.Equal(kept, shortcut.Label);
    }

    [Fact]
    public void TheNameSurvivesASaveAndACopy()
    {
        var settings = new AppSettings { DeviceShortcuts = [new DeviceShortcut { Label = "Office printers" }] };

        string json = JsonSerializer.Serialize(settings);
        Assert.Contains("\"label\":\"Office printers\"", json);

        DeviceShortcut back = Assert.Single(JsonSerializer.Deserialize<AppSettings>(json)!.DeviceShortcuts);
        Assert.Equal("Office printers", back.Label);
        Assert.Equal("Office printers", back.Clone().Label);
    }
}
