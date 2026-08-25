// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;

namespace FateTakesYouHome.HomeAssistant.Models;

/// <summary>An area from <c>config/area_registry/list</c>.</summary>
public sealed class HaArea
{
    [JsonPropertyName("area_id")]
    public string AreaId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("floor_id")]
    public string? FloorId { get; init; }

    [JsonPropertyName("aliases")]
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
}

/// <summary>A floor from <c>config/floor_registry/list</c>. Absent on older cores.</summary>
public sealed class HaFloor
{
    [JsonPropertyName("floor_id")]
    public string FloorId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("level")]
    public int? Level { get; init; }
}

/// <summary>A device from <c>config/device_registry/list</c>.</summary>
public sealed class HaDevice
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("name_by_user")]
    public string? NameByUser { get; init; }

    [JsonPropertyName("area_id")]
    public string? AreaId { get; init; }

    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("disabled_by")]
    public string? DisabledBy { get; init; }

    [JsonIgnore]
    public string DisplayName => NameByUser ?? Name ?? Id;
}

/// <summary>An entry from <c>config/entity_registry/list</c>.</summary>
/// <remarks>
/// The entity registry is what maps an entity to an area. Entities usually inherit their area from
/// their device, so resolving an entity's area means: entity's own <c>area_id</c> first, then the
/// area of the device it belongs to.
/// </remarks>
public sealed class HaEntityRegistryEntry
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; init; } = string.Empty;

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("area_id")]
    public string? AreaId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("platform")]
    public string? Platform { get; init; }

    [JsonPropertyName("disabled_by")]
    public string? DisabledBy { get; init; }

    [JsonPropertyName("hidden_by")]
    public string? HiddenBy { get; init; }

    [JsonPropertyName("entity_category")]
    public string? EntityCategory { get; init; }

    [JsonIgnore]
    public bool IsDisabled => !string.IsNullOrEmpty(DisabledBy);

    [JsonIgnore]
    public bool IsHidden => !string.IsNullOrEmpty(HiddenBy);

    /// <summary>
    /// True for diagnostic/config entities. These are real and useful, but they should not
    /// clutter the default control surface.
    /// </summary>
    [JsonIgnore]
    public bool IsAuxiliary => EntityCategory is "config" or "diagnostic";
}

/// <summary>A callable service, from <c>get_services</c>.</summary>
public sealed class HaService
{
    public required string Domain { get; init; }

    public required string Service { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Raw field schema. Rendered generically by the service console.</summary>
    public JsonElement? Fields { get; init; }

    public string FullName => $"{Domain}.{Service}";
}

/// <summary>The <c>config</c> payload returned after authentication.</summary>
public sealed class HaConfig
{
    [JsonPropertyName("location_name")]
    public string? LocationName { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("time_zone")]
    public string? TimeZone { get; init; }

    [JsonPropertyName("unit_system")]
    public JsonElement? UnitSystem { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }
}

/// <summary>The authenticated user, from <c>auth/current_user</c>.</summary>
public sealed class HaUser
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("is_admin")]
    public bool IsAdmin { get; init; }

    [JsonPropertyName("is_owner")]
    public bool IsOwner { get; init; }
}
