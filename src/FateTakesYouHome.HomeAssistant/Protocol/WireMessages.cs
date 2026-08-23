using System.Text.Json;
using System.Text.Json.Serialization;

namespace FateTakesYouHome.HomeAssistant.Protocol;

/// <summary>Message type discriminators used by the Home Assistant WebSocket API.</summary>
internal static class WireType
{
    public const string AuthRequired = "auth_required";
    public const string Auth = "auth";
    public const string AuthOk = "auth_ok";
    public const string AuthInvalid = "auth_invalid";
    public const string Result = "result";
    public const string Event = "event";
    public const string Ping = "ping";
    public const string Pong = "pong";
}

/// <summary>The envelope every inbound frame shares. Parsed first to decide how to route.</summary>
internal sealed class InboundEnvelope
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    public WireError? Error { get; init; }

    [JsonPropertyName("event")]
    public JsonElement? Event { get; init; }

    [JsonPropertyName("ha_version")]
    public string? HaVersion { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

internal sealed class WireError
{
    [JsonPropertyName("code")]
    public JsonElement Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    public string CodeAsString => Code.ValueKind switch
    {
        JsonValueKind.String => Code.GetString() ?? "unknown",
        JsonValueKind.Number => Code.ToString(),
        _ => "unknown",
    };
}

/// <summary>The <c>state_changed</c> event payload.</summary>
internal sealed class StateChangedData
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; init; } = string.Empty;

    [JsonPropertyName("old_state")]
    public Models.HaEntityState? OldState { get; init; }

    [JsonPropertyName("new_state")]
    public Models.HaEntityState? NewState { get; init; }
}

internal sealed class EventEnvelope
{
    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public JsonElement Data { get; init; }

    [JsonPropertyName("time_fired")]
    public DateTimeOffset? TimeFired { get; init; }
}

/// <summary>Shared serializer settings. Home Assistant speaks snake_case and rejects nulls poorly.</summary>
internal static class WireJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
