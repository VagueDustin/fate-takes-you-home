using System.Text.Json.Serialization;

namespace FateTakesYouHome.Models;

/// <summary>What a widget on the home screen or tray panel is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetKind>))]
public enum WidgetKind
{
    /// <summary>One entity, with its full tile: state, control, slider when it has one.</summary>
    Tile,

    /// <summary>A single button that performs one action.</summary>
    QuickAction,

    /// <summary>The rooms grid: every area with lights, lit first.</summary>
    Rooms,

    /// <summary>The whole-house activity counts.</summary>
    Activity,

    /// <summary>A history graph of one numeric entity.</summary>
    Sparkline,
}

/// <summary>
/// One widget placed on a grid: what it is, and which cells it covers.
/// </summary>
/// <remarks>
/// Coordinates are grid cells, not pixels — the grid itself stretches with the window, which is
/// what keeps one layout working across monitor sizes. Position and span are clamped on load so a
/// file edited by hand cannot put a widget off the edge of the world.
/// </remarks>
public sealed class WidgetSpec
{
    [JsonPropertyName("kind")]
    public WidgetKind Kind { get; set; }

    /// <summary>The entity a Tile, Sparkline, or entity-flavoured QuickAction points at.</summary>
    [JsonPropertyName("entityId")]
    public string? EntityId { get; set; }

    /// <summary>A built-in action name for QuickAction. Currently "allLightsOff".</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("w")]
    public int W { get; set; } = 2;

    [JsonPropertyName("h")]
    public int H { get; set; } = 1;

    /// <summary>How far back a Sparkline looks.</summary>
    [JsonPropertyName("hours")]
    public int Hours { get; set; } = 24;

    public const string AllLightsOffAction = "allLightsOff";

    public WidgetSpec Clone() => new()
    {
        Kind = Kind,
        EntityId = EntityId,
        Action = Action,
        X = X,
        Y = Y,
        W = W,
        H = H,
        Hours = Hours,
    };

    /// <summary>Forces the spec inside a grid of the given width.</summary>
    public void ClampTo(int columns)
    {
        W = Math.Clamp(W, 1, columns);
        H = Math.Clamp(H, 1, 6);
        X = Math.Clamp(X, 0, columns - W);
        Y = Math.Max(0, Y);
        Hours = Math.Clamp(Hours, 1, 168);
    }
}
