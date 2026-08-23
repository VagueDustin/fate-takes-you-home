using FateTakesYouHome.HomeAssistant.Models;

namespace FateTakesYouHome.HomeAssistant;

/// <summary>
/// Maps user intent ("turn this on", "set it to 40%") onto the correct Home Assistant service call
/// for the entity's domain.
/// </summary>
/// <remarks>
/// Most domains answer <c>homeassistant.turn_on</c>, but the interesting ones do not: activating a
/// scene, running a script and firing an automation are three different services with three
/// different meanings. Centralising that here keeps the mapping out of every view model.
/// </remarks>
public static class HaControl
{
    /// <summary>Turns an entity on, using whatever "on" means for its domain.</summary>
    public static Task TurnOnAsync(this HaClient client, string entityId, CancellationToken ct = default)
    {
        string domain = DomainOf(entityId);

        return domain switch
        {
            HaDomains.Scene => client.CallServiceAsync(HaDomains.Scene, "turn_on", entityId, ct: ct),
            HaDomains.Script => client.CallServiceAsync(HaDomains.Script, "turn_on", entityId, ct: ct),
            HaDomains.Button => client.CallServiceAsync(HaDomains.Button, "press", entityId, ct: ct),
            HaDomains.InputButton => client.CallServiceAsync(HaDomains.InputButton, "press", entityId, ct: ct),
            HaDomains.Cover => client.CallServiceAsync(HaDomains.Cover, "open_cover", entityId, ct: ct),
            HaDomains.Valve => client.CallServiceAsync(HaDomains.Valve, "open_valve", entityId, ct: ct),
            HaDomains.Lock => client.CallServiceAsync(HaDomains.Lock, "unlock", entityId, ct: ct),
            HaDomains.Vacuum => client.CallServiceAsync(HaDomains.Vacuum, "start", entityId, ct: ct),
            _ => client.CallServiceAsync("homeassistant", "turn_on", entityId, ct: ct),
        };
    }

    /// <summary>Turns an entity off. Momentary domains have no off and are ignored.</summary>
    public static Task TurnOffAsync(this HaClient client, string entityId, CancellationToken ct = default)
    {
        string domain = DomainOf(entityId);

        return domain switch
        {
            HaDomains.Scene or HaDomains.Button or HaDomains.InputButton => Task.CompletedTask,
            HaDomains.Script => client.CallServiceAsync(HaDomains.Script, "turn_off", entityId, ct: ct),
            HaDomains.Cover => client.CallServiceAsync(HaDomains.Cover, "close_cover", entityId, ct: ct),
            HaDomains.Valve => client.CallServiceAsync(HaDomains.Valve, "close_valve", entityId, ct: ct),
            HaDomains.Lock => client.CallServiceAsync(HaDomains.Lock, "lock", entityId, ct: ct),
            HaDomains.Vacuum => client.CallServiceAsync(HaDomains.Vacuum, "return_to_base", entityId, ct: ct),
            _ => client.CallServiceAsync("homeassistant", "turn_off", entityId, ct: ct),
        };
    }

    /// <summary>
    /// Flips an entity. Uses the observed state rather than <c>toggle</c> for domains where the
    /// two directions are different services.
    /// </summary>
    public static Task ToggleAsync(
        this HaClient client, HaEntityState state, CancellationToken ct = default)
    {
        string domain = state.Domain;

        if (HaDomains.Momentary.Contains(domain))
        {
            return client.TurnOnAsync(state.EntityId, ct);
        }

        if (domain is HaDomains.Cover or HaDomains.Valve or HaDomains.Lock or HaDomains.Vacuum)
        {
            return state.IsOn
                ? client.TurnOffAsync(state.EntityId, ct)
                : client.TurnOnAsync(state.EntityId, ct);
        }

        return client.CallServiceAsync("homeassistant", "toggle", state.EntityId, ct: ct);
    }

    // ------------------------------------------------------------------ lights

    /// <summary>Sets brightness as a percentage. 0 turns the light off, as Home Assistant expects.</summary>
    public static Task SetLightBrightnessAsync(
        this HaClient client, string entityId, double percent, CancellationToken ct = default)
    {
        int pct = (int)Math.Round(Math.Clamp(percent, 0, 100), MidpointRounding.AwayFromZero);

        if (pct <= 0)
        {
            return client.CallServiceAsync(HaDomains.Light, "turn_off", entityId, ct: ct);
        }

        return client.CallServiceAsync(
            HaDomains.Light,
            "turn_on",
            entityId,
            new Dictionary<string, object?> { ["brightness_pct"] = pct },
            ct);
    }

    /// <summary>Sets a white point in kelvin.</summary>
    public static Task SetLightColorTempAsync(
        this HaClient client, string entityId, int kelvin, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Light,
            "turn_on",
            entityId,
            new Dictionary<string, object?> { ["color_temp_kelvin"] = kelvin },
            ct);

    /// <summary>Sets an RGB colour.</summary>
    public static Task SetLightRgbAsync(
        this HaClient client, string entityId, byte r, byte g, byte b, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Light,
            "turn_on",
            entityId,
            new Dictionary<string, object?> { ["rgb_color"] = new[] { (int)r, g, b } },
            ct);

    /// <summary>Applies a named effect from the light's <c>effect_list</c>.</summary>
    public static Task SetLightEffectAsync(
        this HaClient client, string entityId, string effect, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Light,
            "turn_on",
            entityId,
            new Dictionary<string, object?> { ["effect"] = effect },
            ct);

    // ------------------------------------------------------------------ automations and scripts

    /// <summary>
    /// Fires an automation's actions now.
    /// </summary>
    /// <param name="skipCondition">
    /// When true (Home Assistant's own default) the automation's conditions are bypassed, which is
    /// almost always what someone pressing a button by hand means.
    /// </param>
    public static Task TriggerAutomationAsync(
        this HaClient client, string entityId, bool skipCondition = true, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Automation,
            "trigger",
            entityId,
            new Dictionary<string, object?> { ["skip_condition"] = skipCondition },
            ct);

    /// <summary>Enables or disables an automation without firing it.</summary>
    public static Task SetAutomationEnabledAsync(
        this HaClient client, string entityId, bool enabled, CancellationToken ct = default) =>
        client.CallServiceAsync(HaDomains.Automation, enabled ? "turn_on" : "turn_off", entityId, ct: ct);

    /// <summary>Runs a script, optionally passing variables to it.</summary>
    public static Task RunScriptAsync(
        this HaClient client,
        string entityId,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken ct = default) =>
        client.CallServiceAsync(HaDomains.Script, "turn_on", entityId, variables, ct);

    /// <summary>Activates a scene, optionally over a transition.</summary>
    public static Task ActivateSceneAsync(
        this HaClient client, string entityId, double? transitionSeconds = null, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Scene,
            "turn_on",
            entityId,
            transitionSeconds is { } t
                ? new Dictionary<string, object?> { ["transition"] = t }
                : null,
            ct);

    // ------------------------------------------------------------------ covers

    public static Task SetCoverPositionAsync(
        this HaClient client, string entityId, double percent, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Cover,
            "set_cover_position",
            entityId,
            new Dictionary<string, object?> { ["position"] = (int)Math.Round(Math.Clamp(percent, 0, 100)) },
            ct);

    public static Task SetCoverTiltAsync(
        this HaClient client, string entityId, double percent, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Cover,
            "set_cover_tilt_position",
            entityId,
            new Dictionary<string, object?>
            {
                ["tilt_position"] = (int)Math.Round(Math.Clamp(percent, 0, 100)),
            },
            ct);

    public static Task StopCoverAsync(this HaClient client, string entityId, CancellationToken ct = default) =>
        client.CallServiceAsync(HaDomains.Cover, "stop_cover", entityId, ct: ct);

    // ------------------------------------------------------------------ climate

    public static Task SetClimateTemperatureAsync(
        this HaClient client, string entityId, double temperature, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Climate,
            "set_temperature",
            entityId,
            new Dictionary<string, object?> { ["temperature"] = temperature },
            ct);

    public static Task SetClimateTemperatureRangeAsync(
        this HaClient client, string entityId, double low, double high, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Climate,
            "set_temperature",
            entityId,
            new Dictionary<string, object?>
            {
                ["target_temp_low"] = Math.Min(low, high),
                ["target_temp_high"] = Math.Max(low, high),
            },
            ct);

    public static Task SetClimateHvacModeAsync(
        this HaClient client, string entityId, string mode, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Climate,
            "set_hvac_mode",
            entityId,
            new Dictionary<string, object?> { ["hvac_mode"] = mode },
            ct);

    public static Task SetClimateFanModeAsync(
        this HaClient client, string entityId, string mode, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Climate,
            "set_fan_mode",
            entityId,
            new Dictionary<string, object?> { ["fan_mode"] = mode },
            ct);

    public static Task SetClimatePresetAsync(
        this HaClient client, string entityId, string preset, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Climate,
            "set_preset_mode",
            entityId,
            new Dictionary<string, object?> { ["preset_mode"] = preset },
            ct);

    // ------------------------------------------------------------------ fans

    public static Task SetFanPercentageAsync(
        this HaClient client, string entityId, double percent, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Fan,
            "set_percentage",
            entityId,
            new Dictionary<string, object?> { ["percentage"] = (int)Math.Round(Math.Clamp(percent, 0, 100)) },
            ct);

    public static Task SetFanOscillatingAsync(
        this HaClient client, string entityId, bool oscillating, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Fan,
            "oscillate",
            entityId,
            new Dictionary<string, object?> { ["oscillating"] = oscillating },
            ct);

    public static Task SetFanPresetAsync(
        this HaClient client, string entityId, string preset, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.Fan,
            "set_preset_mode",
            entityId,
            new Dictionary<string, object?> { ["preset_mode"] = preset },
            ct);

    // ------------------------------------------------------------------ locks

    public static Task LockAsync(this HaClient client, string entityId, CancellationToken ct = default) =>
        client.CallServiceAsync(HaDomains.Lock, "lock", entityId, ct: ct);

    public static Task UnlockAsync(this HaClient client, string entityId, CancellationToken ct = default) =>
        client.CallServiceAsync(HaDomains.Lock, "unlock", entityId, ct: ct);

    // ------------------------------------------------------------------ media players

    public static Task MediaPlayPauseAsync(
        this HaClient client, string entityId, CancellationToken ct = default) =>
        client.CallServiceAsync(HaDomains.MediaPlayer, "media_play_pause", entityId, ct: ct);

    public static Task MediaNextAsync(this HaClient client, string entityId, CancellationToken ct = default) =>
        client.CallServiceAsync(HaDomains.MediaPlayer, "media_next_track", entityId, ct: ct);

    public static Task MediaPreviousAsync(
        this HaClient client, string entityId, CancellationToken ct = default) =>
        client.CallServiceAsync(HaDomains.MediaPlayer, "media_previous_track", entityId, ct: ct);

    /// <summary>Sets volume from a 0–100 percentage; the service itself takes 0.0–1.0.</summary>
    public static Task SetMediaVolumeAsync(
        this HaClient client, string entityId, double percent, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.MediaPlayer,
            "volume_set",
            entityId,
            new Dictionary<string, object?> { ["volume_level"] = Math.Clamp(percent, 0, 100) / 100d },
            ct);

    public static Task SetMediaMutedAsync(
        this HaClient client, string entityId, bool muted, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.MediaPlayer,
            "volume_mute",
            entityId,
            new Dictionary<string, object?> { ["is_volume_muted"] = muted },
            ct);

    public static Task SelectMediaSourceAsync(
        this HaClient client, string entityId, string source, CancellationToken ct = default) =>
        client.CallServiceAsync(
            HaDomains.MediaPlayer,
            "select_source",
            entityId,
            new Dictionary<string, object?> { ["source"] = source },
            ct);

    // ------------------------------------------------------------------ numbers, selects, text

    public static Task SetNumberAsync(
        this HaClient client, string entityId, double value, CancellationToken ct = default) =>
        client.CallServiceAsync(
            DomainOf(entityId),
            "set_value",
            entityId,
            new Dictionary<string, object?> { ["value"] = value },
            ct);

    public static Task SelectOptionAsync(
        this HaClient client, string entityId, string option, CancellationToken ct = default) =>
        client.CallServiceAsync(
            DomainOf(entityId),
            "select_option",
            entityId,
            new Dictionary<string, object?> { ["option"] = option },
            ct);

    public static Task SetTextAsync(
        this HaClient client, string entityId, string value, CancellationToken ct = default) =>
        client.CallServiceAsync(
            DomainOf(entityId),
            "set_value",
            entityId,
            new Dictionary<string, object?> { ["value"] = value },
            ct);

    // ------------------------------------------------------------------ vacuums

    public static Task VacuumStartAsync(this HaClient client, string entityId, CancellationToken ct = default) =>
        client.CallServiceAsync(HaDomains.Vacuum, "start", entityId, ct: ct);

    public static Task VacuumPauseAsync(this HaClient client, string entityId, CancellationToken ct = default) =>
        client.CallServiceAsync(HaDomains.Vacuum, "pause", entityId, ct: ct);

    public static Task VacuumReturnHomeAsync(
        this HaClient client, string entityId, CancellationToken ct = default) =>
        client.CallServiceAsync(HaDomains.Vacuum, "return_to_base", entityId, ct: ct);

    public static Task VacuumLocateAsync(this HaClient client, string entityId, CancellationToken ct = default) =>
        client.CallServiceAsync(HaDomains.Vacuum, "locate", entityId, ct: ct);

    private static string DomainOf(string entityId)
    {
        int dot = entityId.IndexOf('.');
        return dot > 0 ? entityId[..dot] : entityId;
    }
}
