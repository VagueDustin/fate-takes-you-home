// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;

namespace FateTakesYouHome.Models;

/// <summary>
/// Reads an enum in the settings file by name, without letting a name it does not know break the
/// whole file.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="JsonStringEnumConverter{TEnum}"/> throws on a name it does not know, and a throw
/// anywhere in the settings file sets the whole file aside and starts over from defaults: the
/// server, the token, every pin, both layouts. One mistyped value, or a file written by a newer
/// version with a member this one lacks, is no reason for that. Anything unrecognised reads as a
/// value outside the enum instead, and <see cref="Services.SettingsService"/> repairs it after
/// loading. The converter cannot choose the repair itself: the two tray gestures have different
/// defaults, a widget of an unknown kind has to be removed rather than replaced, and a device
/// shortcut's action is not guessed at (see <see cref="DeviceShortcut.Normalise"/>).
/// </para>
/// <para>
/// Names are written exactly as <see cref="JsonStringEnumConverter{TEnum}"/> wrote them, so a file
/// saved by this version still reads in one from before this converter.
/// </para>
/// </remarks>
internal sealed class TolerantEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    // None of the enums read this way has a negative member, so this cannot be mistaken for one.
    private static readonly TEnum Unrecognised = (TEnum)Enum.ToObject(typeof(TEnum), -1);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                // Compared name by name. Enum.TryParse would also take "3" and "Tile, Rooms",
                // neither of which anybody means by a setting.
                string? name = reader.GetString();

                foreach (TEnum value in Enum.GetValues<TEnum>())
                {
                    if (string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return value;
                    }
                }

                return Unrecognised;

            case JsonTokenType.Number:
                // JsonStringEnumConverter took a number too, so a file edited by hand may hold one.
                // A number no member has is left for the repair, the same as an unknown name.
                return reader.TryGetInt32(out int number)
                    ? (TEnum)Enum.ToObject(typeof(TEnum), number)
                    : Unrecognised;

            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                // The rest of the value still has to be read past, or the next property is lost.
                reader.Skip();
                return Unrecognised;

            default:
                return Unrecognised;
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
