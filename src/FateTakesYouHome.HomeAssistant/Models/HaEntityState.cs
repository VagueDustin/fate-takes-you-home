using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FateTakesYouHome.HomeAssistant.Models;

/// <summary>
/// A single entity's state as reported by Home Assistant.
/// </summary>
/// <remarks>
/// Attributes are intentionally left as raw <see cref="JsonElement"/> values. Home Assistant's
/// attribute bag is open-ended and integration-defined; typing it would guarantee data loss the
/// first time somebody installs a custom component. The <c>Attr*</c> helpers cover the shapes we
/// actually read.
/// </remarks>
public sealed class HaEntityState
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("attributes")]
    public ImmutableDictionary<string, JsonElement> Attributes { get; init; }
        = ImmutableDictionary<string, JsonElement>.Empty;

    [JsonPropertyName("last_changed")]
    public DateTimeOffset? LastChanged { get; init; }

    [JsonPropertyName("last_updated")]
    public DateTimeOffset? LastUpdated { get; init; }

    /// <summary>The part of the entity id before the dot, e.g. <c>light</c>.</summary>
    [JsonIgnore]
    public string Domain
    {
        get
        {
            int dot = EntityId.IndexOf('.');
            return dot > 0 ? EntityId[..dot] : string.Empty;
        }
    }

    /// <summary>The part of the entity id after the dot, e.g. <c>kitchen_ceiling</c>.</summary>
    [JsonIgnore]
    public string ObjectId
    {
        get
        {
            int dot = EntityId.IndexOf('.');
            return dot > 0 ? EntityId[(dot + 1)..] : EntityId;
        }
    }

    /// <summary>Friendly name if the integration supplied one, otherwise a title-cased object id.</summary>
    [JsonIgnore]
    public string FriendlyName =>
        AttrString("friendly_name") ?? HumaniseObjectId(ObjectId);

    /// <summary>True when Home Assistant cannot currently reach the entity.</summary>
    [JsonIgnore]
    public bool IsUnavailable =>
        State is "unavailable" or "unknown" or "";

    /// <summary>
    /// Best-effort "is this thing on" across the domains that have a binary notion of on.
    /// </summary>
    [JsonIgnore]
    public bool IsOn => State switch
    {
        "on" or "open" or "opening" or "home" or "playing" or "cleaning" or "active" => true,
        "unlocked" => true,
        "heat" or "cool" or "heat_cool" or "auto" or "dry" or "fan_only" => true,
        _ => false,
    };

    /// <summary>The integration-declared feature bitmask, or 0 when absent.</summary>
    [JsonIgnore]
    public int SupportedFeatures => AttrInt("supported_features") ?? 0;

    /// <summary>Returns true when <paramref name="flag"/> is set in <see cref="SupportedFeatures"/>.</summary>
    public bool Supports(int flag) => (SupportedFeatures & flag) == flag;

    public JsonElement? Attr(string name) =>
        Attributes.TryGetValue(name, out JsonElement value) ? value : null;

    public string? AttrString(string name)
    {
        if (!Attributes.TryGetValue(name, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public double? AttrDouble(string name)
    {
        if (!Attributes.TryGetValue(name, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(
                v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
            _ => null,
        };
    }

    public int? AttrInt(string name)
    {
        double? d = AttrDouble(name);
        return d is null ? null : (int)Math.Round(d.Value, MidpointRounding.AwayFromZero);
    }

    public bool? AttrBool(string name)
    {
        if (!Attributes.TryGetValue(name, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out bool b) ? b : null,
            _ => null,
        };
    }

    public IReadOnlyList<string> AttrStringList(string name)
    {
        if (!Attributes.TryGetValue(name, out JsonElement v) || v.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>(v.GetArrayLength());
        foreach (JsonElement item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? s = item.GetString();
                if (s is not null)
                {
                    list.Add(s);
                }
            }
        }

        return list;
    }

    /// <summary>Reads an <c>[r, g, b]</c> attribute such as <c>rgb_color</c>.</summary>
    public (byte R, byte G, byte B)? AttrRgb(string name)
    {
        if (!Attributes.TryGetValue(name, out JsonElement v)
            || v.ValueKind != JsonValueKind.Array
            || v.GetArrayLength() < 3)
        {
            return null;
        }

        Span<byte> channels = stackalloc byte[3];
        int i = 0;
        foreach (JsonElement item in v.EnumerateArray())
        {
            if (i >= 3)
            {
                break;
            }

            if (item.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            channels[i++] = (byte)Math.Clamp(item.GetDouble(), 0, 255);
        }

        return i == 3 ? (channels[0], channels[1], channels[2]) : null;
    }

    /// <summary>
    /// Turns <c>kitchen_ceiling</c> into <c>Kitchen Ceiling</c>.
    /// </summary>
    /// <remarks>
    /// Public because the UI needs it for more than friendly names: domain headings, enum-valued
    /// states and select options all arrive as snake_case and have to be shown to a person.
    /// </remarks>
    public static string HumaniseObjectId(string objectId)
    {
        if (string.IsNullOrEmpty(objectId))
        {
            return string.Empty;
        }

        string[] words = objectId.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
