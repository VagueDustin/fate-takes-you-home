using System.Text.Json;
using FateTakesYouHome.HomeAssistant.Models;
using FateTakesYouHome.HomeAssistant.Protocol;

namespace FateTakesYouHome.HomeAssistant;

/// <summary>
/// The typed command surface over <see cref="HaClient"/>'s raw request/reply channel.
/// </summary>
public static class HaCommands
{
    /// <summary>Reads the complete state machine. Called on connect and after every reconnect.</summary>
    public static async Task<IReadOnlyList<HaEntityState>> GetStatesAsync(
        this HaClient client, CancellationToken ct = default)
    {
        JsonElement? result = await client
            .SendCommandAsync(new Dictionary<string, object?> { ["type"] = "get_states" }, ct)
            .ConfigureAwait(false);

        return Deserialize<List<HaEntityState>>(result) ?? [];
    }

    /// <summary>Reads the core configuration — location name, version, unit system.</summary>
    public static async Task<HaConfig?> GetConfigAsync(
        this HaClient client, CancellationToken ct = default)
    {
        JsonElement? result = await client
            .SendCommandAsync(new Dictionary<string, object?> { ["type"] = "get_config" }, ct)
            .ConfigureAwait(false);

        return Deserialize<HaConfig>(result);
    }

    /// <summary>Identifies the account behind the access token. Used to confirm a good setup.</summary>
    public static async Task<HaUser?> GetCurrentUserAsync(
        this HaClient client, CancellationToken ct = default)
    {
        JsonElement? result = await client
            .SendCommandAsync(new Dictionary<string, object?> { ["type"] = "auth/current_user" }, ct)
            .ConfigureAwait(false);

        return Deserialize<HaUser>(result);
    }

    public static async Task<IReadOnlyList<HaArea>> GetAreasAsync(
        this HaClient client, CancellationToken ct = default)
    {
        JsonElement? result = await client
            .SendCommandAsync(
                new Dictionary<string, object?> { ["type"] = "config/area_registry/list" }, ct)
            .ConfigureAwait(false);

        return Deserialize<List<HaArea>>(result) ?? [];
    }

    /// <summary>
    /// Reads the floor registry. Floors arrived in core 2024.4, so an older server answering
    /// "unknown command" is normal rather than an error.
    /// </summary>
    public static async Task<IReadOnlyList<HaFloor>> GetFloorsAsync(
        this HaClient client, CancellationToken ct = default)
    {
        try
        {
            JsonElement? result = await client
                .SendCommandAsync(
                    new Dictionary<string, object?> { ["type"] = "config/floor_registry/list" }, ct)
                .ConfigureAwait(false);

            return Deserialize<List<HaFloor>>(result) ?? [];
        }
        catch (HaCommandException ex) when (ex.Code is "unknown_command" or "not_found")
        {
            return [];
        }
    }

    public static async Task<IReadOnlyList<HaDevice>> GetDevicesAsync(
        this HaClient client, CancellationToken ct = default)
    {
        JsonElement? result = await client
            .SendCommandAsync(
                new Dictionary<string, object?> { ["type"] = "config/device_registry/list" }, ct)
            .ConfigureAwait(false);

        return Deserialize<List<HaDevice>>(result) ?? [];
    }

    public static async Task<IReadOnlyList<HaEntityRegistryEntry>> GetEntityRegistryAsync(
        this HaClient client, CancellationToken ct = default)
    {
        JsonElement? result = await client
            .SendCommandAsync(
                new Dictionary<string, object?> { ["type"] = "config/entity_registry/list" }, ct)
            .ConfigureAwait(false);

        return Deserialize<List<HaEntityRegistryEntry>>(result) ?? [];
    }

    /// <summary>
    /// Reads every callable service. The wire shape is a two-level object keyed by domain then
    /// service name, which this flattens into a list.
    /// </summary>
    public static async Task<IReadOnlyList<HaService>> GetServicesAsync(
        this HaClient client, CancellationToken ct = default)
    {
        JsonElement? result = await client
            .SendCommandAsync(new Dictionary<string, object?> { ["type"] = "get_services" }, ct)
            .ConfigureAwait(false);

        if (result is not { ValueKind: JsonValueKind.Object } root)
        {
            return [];
        }

        var services = new List<HaService>(512);

        foreach (JsonProperty domain in root.EnumerateObject())
        {
            if (domain.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (JsonProperty service in domain.Value.EnumerateObject())
            {
                JsonElement body = service.Value;

                services.Add(new HaService
                {
                    Domain = domain.Name,
                    Service = service.Name,
                    Name = ReadString(body, "name"),
                    Description = ReadString(body, "description"),
                    Fields = body.ValueKind == JsonValueKind.Object
                             && body.TryGetProperty("fields", out JsonElement fields)
                        ? fields.Clone()
                        : null,
                });
            }
        }

        return services;
    }

    /// <summary>
    /// Invokes a service.
    /// </summary>
    /// <param name="target">
    /// The <c>target</c> selector — normally <c>{ ["entity_id"] = "light.kitchen" }</c>. Passing
    /// null calls the service with no target, which is correct for things like
    /// <c>homeassistant.restart</c>.
    /// </param>
    /// <param name="data">Service-specific fields, e.g. <c>brightness_pct</c>.</param>
    public static async Task CallServiceAsync(
        this HaClient client,
        string domain,
        string service,
        IReadOnlyDictionary<string, object?>? target = null,
        IReadOnlyDictionary<string, object?>? data = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var command = new Dictionary<string, object?>
        {
            ["type"] = "call_service",
            ["domain"] = domain,
            ["service"] = service,
        };

        if (target is { Count: > 0 })
        {
            command["target"] = target;
        }

        if (data is { Count: > 0 })
        {
            command["service_data"] = data;
        }

        await client.SendCommandAsync(command, ct).ConfigureAwait(false);
    }

    /// <summary>Convenience overload for the overwhelmingly common single-entity case.</summary>
    public static Task CallServiceAsync(
        this HaClient client,
        string domain,
        string service,
        string entityId,
        IReadOnlyDictionary<string, object?>? data = null,
        CancellationToken ct = default) =>
        client.CallServiceAsync(
            domain,
            service,
            new Dictionary<string, object?> { ["entity_id"] = entityId },
            data,
            ct);

    /// <summary>Round-trips a ping. Useful for a "Test connection" button.</summary>
    public static async Task PingAsync(this HaClient client, CancellationToken ct = default) =>
        await client
            .SendCommandAsync(new Dictionary<string, object?> { ["type"] = WireType.Ping }, ct)
            .ConfigureAwait(false);

    private static T? Deserialize<T>(JsonElement? element)
    {
        if (element is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return default;
        }

        return value.Deserialize<T>(WireJson.Options);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
