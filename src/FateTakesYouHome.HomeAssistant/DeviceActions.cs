// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.ExceptionServices;
using FateTakesYouHome.HomeAssistant.Models;

namespace FateTakesYouHome.HomeAssistant;

/// <summary>What a device shortcut does to the entities it targets.</summary>
public enum DeviceAction
{
    /// <summary>If any target is on, turn them all off; otherwise turn them all on.</summary>
    Toggle,

    TurnOn,

    TurnOff,

    /// <summary>Raise light brightness by a step, as a percentage.</summary>
    BrightnessUp,

    /// <summary>Lower light brightness by a step, as a percentage.</summary>
    BrightnessDown,

    /// <summary>Set light brightness to a level, as a percentage. 0 turns the lights off.</summary>
    SetBrightness,

    PlayPause,

    Pause,

    NextTrack,

    PreviousTrack,

    VolumeUp,

    VolumeDown,

    /// <summary>If any target is audible, mute them all; otherwise unmute them all.</summary>
    ToggleMute,
}

/// <summary>One service call: the service, the entities it targets, and its data.</summary>
public sealed record HaServiceCall(
    string Domain,
    string Service,
    IReadOnlyList<string> EntityIds,
    IReadOnlyDictionary<string, object?>? Data = null);

/// <summary>
/// Turns one action on a set of entities into the service calls that carry it out.
/// </summary>
/// <remarks>
/// <para>
/// Planning is separate from sending so the decisions (which direction a group toggles, which
/// targets an action skips, how calls are batched) are plain functions of entity state and can be
/// tested without a connection.
/// </para>
/// <para>
/// A group behaves as one device. Toggling each member individually is the obvious
/// implementation, and it leaves a half-on group half on forever, with the halves swapping on every
/// press. Deciding once for the whole group, the way Home Assistant's own groups do, converges.
/// </para>
/// </remarks>
public static class DeviceActions
{
    /// <summary>Domains with an on and an off that a shortcut may use in both directions.</summary>
    private static readonly IReadOnlySet<string> Switchable = new HashSet<string>(StringComparer.Ordinal)
    {
        HaDomains.Light, HaDomains.Switch, HaDomains.InputBoolean, HaDomains.Fan, HaDomains.Siren,
        HaDomains.Humidifier, HaDomains.Automation, HaDomains.Script, HaDomains.Cover,
        HaDomains.Valve, HaDomains.Vacuum, HaDomains.MediaPlayer, HaDomains.Climate,
    };

    /// <summary>
    /// True when the action does anything at all to entities of this domain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Locks only ever go one way. A system-wide key is easy to press by accident, from any
    /// application, by anybody at the keyboard; one that can unlock a door is a hazard nobody
    /// would choose. Turning off (locking) is allowed, and a toggle that would turn a group on
    /// skips its locks.
    /// </para>
    /// <para>
    /// This is the domain-level answer the editor uses to warn about skipped targets. Planning
    /// additionally checks what each entity reports it supports.
    /// </para>
    /// </remarks>
    public static bool AppliesTo(DeviceAction action, string domain) => action switch
    {
        DeviceAction.Toggle =>
            Switchable.Contains(domain) || HaDomains.Momentary.Contains(domain) || domain == HaDomains.Lock,
        DeviceAction.TurnOn => Switchable.Contains(domain) || HaDomains.Momentary.Contains(domain),
        DeviceAction.TurnOff => Switchable.Contains(domain) || domain == HaDomains.Lock,
        DeviceAction.BrightnessUp or DeviceAction.BrightnessDown or DeviceAction.SetBrightness =>
            domain == HaDomains.Light,
        _ => domain == HaDomains.MediaPlayer,
    };

    /// <summary>True when any action applies to the domain, so it is worth offering as a target.</summary>
    public static bool IsTargetable(string domain) =>
        Enum.GetValues<DeviceAction>().Any(action => AppliesTo(action, domain));

    /// <summary>True for the actions that take a percentage.</summary>
    public static bool TakesValue(DeviceAction action) =>
        action is DeviceAction.BrightnessUp or DeviceAction.BrightnessDown or DeviceAction.SetBrightness;

    /// <summary>The percentage used when a shortcut does not specify one.</summary>
    public static double DefaultValue(DeviceAction action) =>
        action == DeviceAction.SetBrightness ? 50 : 10;

    /// <summary>
    /// Holds a percentage to its useful range: a step of 0 would do nothing, but a level of 0 is
    /// a legitimate way to say "off".
    /// </summary>
    public static int ClampValue(DeviceAction action, double value) =>
        (int)Math.Round(
            Math.Clamp(value, action == DeviceAction.SetBrightness ? 0 : 1, 100),
            MidpointRounding.AwayFromZero);

    /// <summary>
    /// The calls that perform <paramref name="action"/> on <paramref name="targets"/>. Empty when
    /// no target can take the action.
    /// </summary>
    /// <param name="value">The percentage for actions that take one; null uses the default.</param>
    public static IReadOnlyList<HaServiceCall> Plan(
        DeviceAction action, IEnumerable<HaEntityState> targets, double? value = null)
    {
        ArgumentNullException.ThrowIfNull(targets);

        List<HaEntityState> usable = targets
            .Where(t => !t.IsUnavailable && CanTake(action, t))
            .DistinctBy(t => t.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (usable.Count == 0)
        {
            return [];
        }

        int percent = ClampValue(action, value ?? DefaultValue(action));

        switch (action)
        {
            case DeviceAction.Toggle:
                // Momentary members have no state worth asking about; they fire when the group
                // goes on and sit out when it goes off.
                bool anyOn = usable.Any(t => !HaDomains.Momentary.Contains(t.Domain) && IsActive(t));
                return anyOn ? PlanOff(usable) : PlanOn(usable);

            case DeviceAction.TurnOn:
                return PlanOn(usable);

            case DeviceAction.TurnOff:
                return PlanOff(usable);

            // A step rather than an absolute level: Home Assistant does the arithmetic against the
            // light's real brightness, where reading the local cache and sending a level would
            // race it on every quick second press.
            case DeviceAction.BrightnessUp:
                return [Call(HaDomains.Light, "turn_on", usable, ("brightness_step_pct", percent))];

            case DeviceAction.BrightnessDown:
                return [Call(HaDomains.Light, "turn_on", usable, ("brightness_step_pct", -percent))];

            case DeviceAction.SetBrightness:
                return percent <= 0
                    ? [Call(HaDomains.Light, "turn_off", usable)]
                    : [Call(HaDomains.Light, "turn_on", usable, ("brightness_pct", percent))];

            case DeviceAction.PlayPause:
                return [Call(HaDomains.MediaPlayer, "media_play_pause", usable)];

            case DeviceAction.Pause:
                return [Call(HaDomains.MediaPlayer, "media_pause", usable)];

            case DeviceAction.NextTrack:
                return [Call(HaDomains.MediaPlayer, "media_next_track", usable)];

            case DeviceAction.PreviousTrack:
                return [Call(HaDomains.MediaPlayer, "media_previous_track", usable)];

            case DeviceAction.VolumeUp:
                return [Call(HaDomains.MediaPlayer, "volume_up", usable)];

            case DeviceAction.VolumeDown:
                return [Call(HaDomains.MediaPlayer, "volume_down", usable)];

            case DeviceAction.ToggleMute:
                // Decided by the players that say whether they are muted. One that is switched off
                // says nothing, and counting that silence as audible meant a group with a player
                // turned off could be muted but never unmuted again.
                bool anyAudible = usable.Any(t => t.AttrBool("is_volume_muted") == false)
                    || usable.All(t => t.AttrBool("is_volume_muted") is null);
                return [Call(HaDomains.MediaPlayer, "volume_mute", usable, ("is_volume_muted", anyAudible))];

            default:
                return [];
        }
    }

    /// <summary>
    /// Sends planned calls in order.
    /// </summary>
    /// <remarks>
    /// A refusal does not stop the remaining calls: in a mixed group, a media player that turns
    /// down a service is no reason to leave the lights alone. The first refusal is rethrown once
    /// everything has been tried, so the caller still hears about it. A dropped connection is not
    /// caught, because nothing after it could succeed either.
    /// </remarks>
    public static async Task SendAsync(
        this HaClient client, IReadOnlyList<HaServiceCall> calls, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(calls);

        HaCommandException? firstRefusal = null;

        foreach (HaServiceCall call in calls)
        {
            try
            {
                await client
                    .CallServiceAsync(
                        call.Domain,
                        call.Service,
                        new Dictionary<string, object?> { ["entity_id"] = call.EntityIds },
                        call.Data,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (HaCommandException ex)
            {
                firstRefusal ??= ex;
            }
        }

        if (firstRefusal is not null)
        {
            ExceptionDispatchInfo.Throw(firstRefusal);
        }
    }

    private static bool CanTake(DeviceAction action, HaEntityState target)
    {
        string domain = target.Domain;

        if (!AppliesTo(action, domain))
        {
            return false;
        }

        if (domain == HaDomains.Light && TakesValue(action))
        {
            return HasBrightness(target);
        }

        // Thermostats and fans declare whether they can be switched on and off at all, and Home
        // Assistant refuses the call for one that cannot. Batched with the rest of a group, that
        // refusal would report the whole shortcut as failed over a device it was never going to
        // move.
        if (domain is HaDomains.Climate or HaDomains.Fan
            && action is DeviceAction.Toggle or DeviceAction.TurnOn or DeviceAction.TurnOff)
        {
            (int on, int off) = domain == HaDomains.Climate
                ? (HaFeatures.Climate.TurnOn, HaFeatures.Climate.TurnOff)
                : (HaFeatures.Fan.TurnOn, HaFeatures.Fan.TurnOff);

            return action switch
            {
                DeviceAction.TurnOn => target.Supports(on),
                DeviceAction.TurnOff => target.Supports(off),
                _ => target.Supports(on) && target.Supports(off),
            };
        }

        if (domain != HaDomains.MediaPlayer)
        {
            return true;
        }

        // Media players vary more than any other domain: a speaker may have no power switch, a
        // TV no track skipping. Home Assistant refuses a call naming an entity that lacks the
        // feature, so the ones that lack it are left out rather than failing the whole call.
        return action switch
        {
            DeviceAction.TurnOn => target.Supports(HaFeatures.MediaPlayer.TurnOn),
            DeviceAction.TurnOff => target.Supports(HaFeatures.MediaPlayer.TurnOff),
            DeviceAction.Toggle =>
                target.Supports(HaFeatures.MediaPlayer.TurnOn) && target.Supports(HaFeatures.MediaPlayer.TurnOff),
            DeviceAction.PlayPause =>
                target.Supports(HaFeatures.MediaPlayer.Pause) || target.Supports(HaFeatures.MediaPlayer.Play),
            DeviceAction.Pause => target.Supports(HaFeatures.MediaPlayer.Pause),
            DeviceAction.NextTrack => target.Supports(HaFeatures.MediaPlayer.NextTrack),
            DeviceAction.PreviousTrack => target.Supports(HaFeatures.MediaPlayer.PreviousTrack),
            DeviceAction.VolumeUp or DeviceAction.VolumeDown =>
                target.Supports(HaFeatures.MediaPlayer.VolumeStep) || target.Supports(HaFeatures.MediaPlayer.VolumeSet),
            DeviceAction.ToggleMute => target.Supports(HaFeatures.MediaPlayer.VolumeMute),
            _ => false,
        };
    }

    /// <summary>
    /// Whether a light can dim. A light that reports no colour modes at all is given the benefit
    /// of the doubt; one that reports only on/off is not.
    /// </summary>
    private static bool HasBrightness(HaEntityState light)
    {
        IReadOnlyList<string> modes = light.AttrStringList("supported_color_modes");
        return modes.Count == 0 || modes.Any(HaColorModes.HasBrightness);
    }

    /// <summary>
    /// "On" for the purpose of a group toggle. A paused media player is still switched on, which
    /// <see cref="HaEntityState.IsOn"/> (reading "playing" as on) would get backwards here.
    /// </summary>
    private static bool IsActive(HaEntityState target) =>
        target.Domain == HaDomains.MediaPlayer
            ? target.State is not ("off" or "standby" or "unknown")
            : target.IsOn;

    private static IReadOnlyList<HaServiceCall> PlanOn(IEnumerable<HaEntityState> targets) =>
        Batch(targets
            .Where(t => t.Domain != HaDomains.Lock)
            .Select(t => (Service: HaControl.TurnOnService(t.Domain), t.EntityId)));

    private static IReadOnlyList<HaServiceCall> PlanOff(IEnumerable<HaEntityState> targets)
    {
        var pairs = new List<((string Domain, string Service) Service, string EntityId)>();

        foreach (HaEntityState target in targets)
        {
            if (HaControl.TurnOffService(target.Domain) is { } off)
            {
                pairs.Add((off, target.EntityId));
            }
        }

        return Batch(pairs);
    }

    /// <summary>
    /// One call per distinct service, in the order the services first appear, so a group of
    /// twelve lights is one round trip rather than twelve.
    /// </summary>
    private static IReadOnlyList<HaServiceCall> Batch(
        IEnumerable<((string Domain, string Service) Service, string EntityId)> pairs) =>
        pairs
            .GroupBy(p => p.Service)
            .Select(g => new HaServiceCall(
                g.Key.Domain, g.Key.Service, g.Select(p => p.EntityId).ToArray()))
            .ToList();

    private static HaServiceCall Call(
        string domain, string service, IEnumerable<HaEntityState> targets, params (string Key, object? Value)[] data) =>
        new(
            domain,
            service,
            targets.Select(t => t.EntityId).ToArray(),
            data.Length == 0 ? null : data.ToDictionary(d => d.Key, d => d.Value));
}
