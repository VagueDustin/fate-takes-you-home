// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;
using FateTakesYouHome.HomeAssistant;

namespace FateTakesYouHome.Models;

/// <summary>
/// A system-wide shortcut that acts on chosen devices: a key combination, an action, and the
/// entities it acts on.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="AppSettings.Shortcuts"/>, which maps a fixed set of application
/// actions to one gesture each. These are a user-made list with a shape of their own, and folding
/// them into that map would have meant encoding the action and targets into its keys.
/// </remarks>
public sealed class DeviceShortcut
{
    /// <summary>Prefix that marks a hotkey registration as belonging to a device shortcut.</summary>
    public const string KeyPrefix = "device:";

    /// <summary>Stable identity, so a row survives reordering and hotkey presses find their shortcut.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = NewId();

    /// <summary>
    /// What the person calls this shortcut, such as "Office printers". Null means it is described
    /// by what it does instead. Named "label" in the file, as a pin's own name is.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>"Ctrl+Alt+L" text, or null while the shortcut has no keys yet.</summary>
    [JsonPropertyName("gesture")]
    public string? Gesture { get; set; }

    // The converter sits on the property rather than the enum so the Home Assistant library does
    // not have to know how this application stores its settings.
    [JsonPropertyName("action")]
    [JsonConverter(typeof(DeviceActionConverter))]
    public DeviceAction Action { get; set; } = DeviceAction.Toggle;

    [JsonPropertyName("entityIds")]
    public List<string> EntityIds { get; set; } = [];

    /// <summary>The percentage for actions that take one. Null means the action's default.</summary>
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    /// <summary>What this shortcut registers under with <see cref="Services.HotkeyService"/>.</summary>
    [JsonIgnore]
    public string HotkeyKey => KeyPrefix + Id;

    public DeviceShortcut Clone() => new()
    {
        Id = Id,
        Label = Label,
        Gesture = Gesture,
        Action = Action,
        EntityIds = [.. EntityIds],
        Value = Value,
    };

    /// <summary>
    /// Repairs a list read from a file somebody may have edited by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A shortcut with no targets is kept: it is what a newly added row looks like until somebody
    /// picks a device, and dropping it on the next load would lose a half-made shortcut.
    /// </para>
    /// <para>
    /// An action this version does not recognise, from a typo or from a newer version's file, is
    /// not guessed at. A system-wide key that quietly did something other than what was chosen
    /// would be worse than one that does nothing, so the shortcut keeps its name and devices and
    /// gives up its keys until somebody picks an action for it on the Shortcuts page.
    /// </para>
    /// </remarks>
    public static List<DeviceShortcut> Normalise(List<DeviceShortcut>? shortcuts)
    {
        var result = new List<DeviceShortcut>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (DeviceShortcut? shortcut in shortcuts ?? [])
        {
            if (shortcut is null)
            {
                continue;
            }

            // A duplicated id would make one shortcut's key fire the other's action.
            if (string.IsNullOrWhiteSpace(shortcut.Id) || !ids.Add(shortcut.Id))
            {
                shortcut.Id = NewId();
                ids.Add(shortcut.Id);
            }

            shortcut.Label = string.IsNullOrWhiteSpace(shortcut.Label) ? null : shortcut.Label.Trim();

            if (!Enum.IsDefined(shortcut.Action))
            {
                shortcut.Action = DeviceAction.Toggle;
                shortcut.Gesture = null;
            }

            shortcut.EntityIds = (shortcut.EntityIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            shortcut.Value = shortcut.Value is { } value && double.IsFinite(value)
                ? DeviceActions.ClampValue(shortcut.Action, value)
                : null;

            result.Add(shortcut);
        }

        return result;
    }

    /// <summary>
    /// How messages and screen readers refer to this shortcut: its name when it has one, and
    /// otherwise what it does.
    /// </summary>
    /// <param name="nameOf">Resolves an entity id to the name a person would recognise.</param>
    public string Describe(Func<string, string> nameOf) => Label ?? DescribeAction(nameOf);

    /// <summary>What the shortcut does, in words: "Toggle Desk lamp", "Brightness up 3 devices".</summary>
    /// <param name="nameOf">Resolves an entity id to the name a person would recognise.</param>
    public string DescribeAction(Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(nameOf);

        string targets = EntityIds.Count switch
        {
            0 => "no devices",
            1 => nameOf(EntityIds[0]),
            2 => $"{nameOf(EntityIds[0])} and {nameOf(EntityIds[1])}",
            _ => $"{EntityIds.Count} devices",
        };

        return $"{ActionLabel(Action)} {targets}";
    }

    /// <summary>How each action reads in the editor's list.</summary>
    public static string ActionLabel(DeviceAction action) => action switch
    {
        DeviceAction.Toggle => "Toggle",
        DeviceAction.TurnOn => "Turn on",
        DeviceAction.TurnOff => "Turn off",
        DeviceAction.BrightnessUp => "Brightness up",
        DeviceAction.BrightnessDown => "Brightness down",
        DeviceAction.SetBrightness => "Set brightness",
        DeviceAction.PlayPause => "Play or pause",
        DeviceAction.Pause => "Pause",
        DeviceAction.NextTrack => "Next track",
        DeviceAction.PreviousTrack => "Previous track",
        DeviceAction.VolumeUp => "Volume up",
        DeviceAction.VolumeDown => "Volume down",
        DeviceAction.ToggleMute => "Mute or unmute",
        _ => action.ToString(),
    };

    private static string NewId() => Guid.NewGuid().ToString("N");
}

/// <summary>
/// Reads a device shortcut's action by name, without letting a name it does not know break the
/// whole settings file.
/// </summary>
/// <remarks>
/// <see cref="JsonStringEnumConverter{TEnum}"/> throws on a name it does not know, and a throw
/// anywhere in the settings file sets the whole file aside and starts over from defaults: the
/// server, the token, every pin. One mistyped action, or a file written by a newer version with an
/// action this one lacks, is no reason for that. Anything unrecognised reads as a value outside
/// the enum instead, which <see cref="DeviceShortcut.Normalise"/> then deals with.
/// </remarks>
internal sealed class DeviceActionConverter : JsonConverter<DeviceAction>
{
    /// <summary>What an action this version has no name for reads as.</summary>
    internal const DeviceAction Unrecognised = (DeviceAction)(-1);

    public override DeviceAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                // Compared name by name. Enum.TryParse would also take "3" and "Toggle, TurnOn",
                // neither of which anybody means by an action.
                string? name = reader.GetString();

                foreach (DeviceAction action in Enum.GetValues<DeviceAction>())
                {
                    if (string.Equals(action.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return action;
                    }
                }

                return Unrecognised;

            case JsonTokenType.Number:
                return reader.TryGetInt32(out int number) ? (DeviceAction)number : Unrecognised;

            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                // The rest of the value still has to be read past, or the next property is lost.
                reader.Skip();
                return Unrecognised;

            default:
                return Unrecognised;
        }
    }

    public override void Write(Utf8JsonWriter writer, DeviceAction value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
