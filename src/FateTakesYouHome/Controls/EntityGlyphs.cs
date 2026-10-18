// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Concurrent;
using System.Windows.Media;
using FateTakesYouHome.HomeAssistant.Models;

namespace FateTakesYouHome.Controls;

/// <summary>
/// Line-art icons for entity domains, drawn on a 24-unit grid.
/// </summary>
/// <remarks>
/// <para>
/// Hand-drawn rather than taken from Segoe Fluent Icons. Two reasons: the icon font's coverage of
/// home-automation concepts is poor (there is no thermostat, no cover, no vacuum), and a stroked
/// outline reads as engraved, which is what the charted tier calls for. A filled pictograph would
/// look pasted in.
/// </para>
/// <para>
/// Every path is stroked, not filled, so a single geometry works at any size and in any accent
/// colour. Geometries are frozen and cached: a flyout can hold thirty of these and they must not
/// be re-parsed on every state change.
/// </para>
/// </remarks>
public static class EntityGlyphs
{
    /// <summary>The design grid. Callers scale from this.</summary>
    public const double DesignSize = 24d;

    private static readonly ConcurrentDictionary<string, Geometry> Cache = new(StringComparer.Ordinal);

    /// <summary>Returns the icon for an entity, falling back to a generic mark.</summary>
    public static Geometry For(HaEntityState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        string? deviceClass = state.AttrString("device_class");
        return For(state.Domain, deviceClass);
    }

    public static Geometry For(string domain, string? deviceClass = null)
    {
        string key = deviceClass is { Length: > 0 } ? $"{domain}/{deviceClass}" : domain;

        return Cache.GetOrAdd(key, _ => Build(domain, deviceClass));
    }

    private static Geometry Build(string domain, string? deviceClass)
    {
        string path = Lookup(domain, deviceClass);

        Geometry geometry = Geometry.Parse(Frame + path);
        geometry.Freeze();
        return geometry;
    }

    private static string Lookup(string domain, string? deviceClass) => domain switch
    {
        HaDomains.Light => Light,
        HaDomains.Switch => deviceClass == "outlet" ? Outlet : Switch,
        HaDomains.InputBoolean => Switch,
        HaDomains.Scene => Scene,
        HaDomains.Script => Script,
        HaDomains.Automation => Automation,
        HaDomains.Cover => CoverFor(deviceClass),
        HaDomains.Climate => Climate,
        HaDomains.Fan => Fan,
        HaDomains.Lock => Lock,
        HaDomains.MediaPlayer => deviceClass == "tv" ? Television : Speaker,
        HaDomains.Vacuum => Vacuum,
        HaDomains.Humidifier => Humidity,
        HaDomains.WaterHeater => Climate,
        HaDomains.Valve => Valve,
        HaDomains.Siren => Siren,
        HaDomains.Camera => Camera,
        HaDomains.Person => Person,
        HaDomains.DeviceTracker => Person,
        HaDomains.Button or HaDomains.InputButton => Button,
        HaDomains.Number or HaDomains.InputNumber => Number,
        HaDomains.Select or HaDomains.InputSelect => Select,
        HaDomains.Text or HaDomains.InputText => Text,
        HaDomains.Sensor => SensorFor(deviceClass),
        HaDomains.BinarySensor => BinarySensorFor(deviceClass),
        HaDomains.Update => Update,
        _ => Generic,
    };

    private static string CoverFor(string? deviceClass) => deviceClass switch
    {
        "garage" => Garage,
        "door" or "gate" => Door,
        "window" => Window,
        _ => Blinds,
    };

    private static string SensorFor(string? deviceClass) => deviceClass switch
    {
        "temperature" => Thermometer,
        "humidity" => Humidity,
        "power" or "energy" or "current" or "voltage" => Power,
        "illuminance" => Light,
        "battery" => Battery,
        "pressure" or "atmospheric_pressure" => Gauge,
        _ => Gauge,
    };

    private static string BinarySensorFor(string? deviceClass) => deviceClass switch
    {
        "motion" or "occupancy" or "presence" => Motion,
        "door" or "garage_door" => Door,
        "window" or "opening" => Window,
        "moisture" => Humidity,
        "smoke" or "gas" or "carbon_monoxide" => Siren,
        "battery" => Battery,
        _ => Dot,
    };

    // ==================================================================== the paths
    // Stroked outlines on a 24-unit grid, kept inside a 3-unit margin so they optically match
    // when set beside one another.

    /// <summary>
    /// Two bare move-to points at opposite corners of the grid, prefixed to every glyph.
    /// </summary>
    /// <remarks>
    /// A figure with no segments draws nothing but still counts toward the geometry's bounds, and
    /// a <c>Path</c> with <c>Stretch="Uniform"</c> scales to those bounds. Without the frame every
    /// glyph was stretched to fill the box on its own, so a hash sign came out half as large again
    /// as a light bulb and the 3-unit margin above did nothing.
    /// </remarks>
    private const string Frame = "M0,0 M24,24 ";

    /// <summary>A bulb with a filament and a base.</summary>
    private const string Light =
        "M12,3.2 A6,6 0 0 1 18,9.2 C18,12.2 16.2,13.6 15.4,15.2 L8.6,15.2 "
        + "C7.8,13.6 6,12.2 6,9.2 A6,6 0 0 1 12,3.2 Z "
        + "M9,17.6 L15,17.6 M10,20.2 L14,20.2";

    /// <summary>A rocker switch in its housing.</summary>
    private const string Switch =
        "M5.5,4.5 L18.5,4.5 A1.6,1.6 0 0 1 20.1,6.1 L20.1,17.9 A1.6,1.6 0 0 1 18.5,19.5 "
        + "L5.5,19.5 A1.6,1.6 0 0 1 3.9,17.9 L3.9,6.1 A1.6,1.6 0 0 1 5.5,4.5 Z "
        + "M9,8.2 L15,8.2 L15,12 L9,12 Z";

    /// <summary>A faceplate with a round socket and two pins. Two slots on a square read as a face.</summary>
    private const string Outlet =
        "M6,3.5 L18,3.5 A2.5,2.5 0 0 1 20.5,6 L20.5,18 A2.5,2.5 0 0 1 18,20.5 L6,20.5 "
        + "A2.5,2.5 0 0 1 3.5,18 L3.5,6 A2.5,2.5 0 0 1 6,3.5 Z "
        + "M12,6.6 A5.4,5.4 0 1 1 11.99,6.6 Z M9.9,12 L9.91,12 M14.1,12 L14.11,12";

    /// <summary>A four-point star: the moment a scene is set.</summary>
    private const string Scene =
        "M12,3 L14.1,9.9 L21,12 L14.1,14.1 L12,21 L9.9,14.1 L3,12 L9.9,9.9 Z";

    /// <summary>A document with a play mark: a sequence to run.</summary>
    private const string Script =
        "M6,3.5 L14,3.5 L18.5,8 L18.5,20.5 L6,20.5 Z M14,3.5 L14,8 L18.5,8 "
        + "M10,12.5 L10,17 L14.2,14.75 Z";

    /// <summary>A loop with a spark inside: a rule that runs itself. The old gear broke up below 20 pixels.</summary>
    private const string Automation =
        "M18.9,8 A8,8 0 1 1 12,4 M9.8,1.8 L12.2,4 L9.8,6.2 "
        + "M12.9,7.6 L9.4,12.8 L12.2,12.8 L11.1,16.4 L14.6,11.2 L11.8,11.2 Z";

    /// <summary>A blind with slats.</summary>
    private const string Blinds =
        "M3.5,4 L20.5,4 M4.5,4 L4.5,20 M19.5,4 L19.5,20 "
        + "M4.5,8 L19.5,8 M4.5,12 L19.5,12 M4.5,16 L19.5,16 M4.5,20 L19.5,20";

    private const string Garage =
        "M3.5,10.5 L12,4.5 L20.5,10.5 L20.5,20 L3.5,20 Z "
        + "M6.8,13 L17.2,13 M6.8,16.5 L17.2,16.5 M6.8,20 L17.2,20";

    private const string Door =
        "M6,3.5 L18,3.5 L18,20.5 L6,20.5 Z M14.6,12.4 A0.85,0.85 0 1 0 14.61,12.4 Z";

    private const string Window =
        "M4,4 L20,4 L20,20 L4,20 Z M12,4 L12,20 M4,12 L20,12";

    /// <summary>A thermostat dial with its knob, open at the foot so it is not mistaken for a clock.</summary>
    private const string Climate =
        "M12,3.6 A8.4,8.4 0 1 1 11.99,3.6 Z "
        + "M8.2,15.8 A5.4,5.4 0 1 1 15.8,15.8 M12,12 L14.6,9.4";

    /// <summary>Three blades around a hub.</summary>
    private const string Fan =
        "M12,12 C12,8.4 9.6,3.6 12,3.6 C14.4,3.6 15.2,8 12,12 Z "
        + "M12,12 C15.1,10.2 20.4,9.5 19.2,11.6 C18,13.7 15.4,14.2 12,12 Z "
        + "M12,12 C12.9,15.5 16,19.8 13.9,21 C11.8,22.2 9.4,15.9 12,12 Z "
        + "M12,12 C8.9,13.8 3.6,14.5 4.8,12.4 C6,10.3 8.6,9.8 12,12 Z";

    private const string Lock =
        "M6.5,10.5 L17.5,10.5 L17.5,20 L6.5,20 Z "
        + "M8.8,10.5 L8.8,7.6 A3.2,3.2 0 0 1 15.2,7.6 L15.2,10.5 "
        + "M12,14 L12,16.6";

    private const string Speaker =
        "M6.5,3.8 L17.5,3.8 A1.4,1.4 0 0 1 18.9,5.2 L18.9,18.8 A1.4,1.4 0 0 1 17.5,20.2 "
        + "L6.5,20.2 A1.4,1.4 0 0 1 5.1,18.8 L5.1,5.2 A1.4,1.4 0 0 1 6.5,3.8 Z "
        + "M12,14.2 A3.2,3.2 0 1 0 12.01,14.2 Z M12,7.6 A0.9,0.9 0 1 0 12.01,7.6 Z";

    private const string Television =
        "M3.6,6 L20.4,6 L20.4,17 L3.6,17 Z M8.4,20 L15.6,20 M12,17 L12,20";

    /// <summary>A robot vacuum from above: the body, its bumper line and the sensor.</summary>
    private const string Vacuum =
        "M12,4 A8,8 0 1 1 11.99,4 Z M6.4,9.4 A7,7 0 0 1 17.6,9.4 "
        + "M12,14.2 A1.6,1.6 0 1 1 11.99,14.2 Z";

    private const string Humidity =
        "M12,3.4 C12,3.4 5.6,10.6 5.6,14.6 A6.4,6.4 0 0 0 18.4,14.6 C18.4,10.6 12,3.4 12,3.4 Z";

    /// <summary>The engineering valve symbol: two cones meeting, with a stem and a handwheel.</summary>
    private const string Valve =
        "M4,10 L4,18 L12,14 Z M20,10 L20,18 L12,14 Z M12,14 L12,7 M8.5,7 L15.5,7";

    private const string Siren =
        "M6,17.5 C6,12 8.4,8.5 12,8.5 C15.6,8.5 18,12 18,17.5 Z "
        + "M4.5,20.5 L19.5,20.5 M12,8.5 L12,5.5 M4.6,10.4 L2.8,9.2 M19.4,10.4 L21.2,9.2";

    private const string Camera =
        "M3.6,7.5 L14.4,7.5 L14.4,16.5 L3.6,16.5 Z M14.4,10.8 L20.4,7.8 L20.4,16.2 L14.4,13.2 Z";

    private const string Person =
        "M12,4 A3.9,3.9 0 1 1 11.99,4 Z "
        + "M4.8,20.4 C4.8,16.4 8,14.2 12,14.2 C16,14.2 19.2,16.4 19.2,20.4";

    /// <summary>A push button from above: the cap standing in its bezel.</summary>
    private const string Button =
        "M6.4,4.6 L17.6,4.6 A1.8,1.8 0 0 1 19.4,6.4 L19.4,17.6 A1.8,1.8 0 0 1 17.6,19.4 "
        + "L6.4,19.4 A1.8,1.8 0 0 1 4.6,17.6 L4.6,6.4 A1.8,1.8 0 0 1 6.4,4.6 Z M12,8 A4,4 0 1 1 11.99,8 Z";

    private const string Number =
        "M4.5,9 L19.5,9 M4.5,15 L19.5,15 M9.6,4.5 L8,19.5 M16,4.5 L14.4,19.5";

    /// <summary>A dropdown: a field, its value and the chevron.</summary>
    private const string Select =
        "M5,7.5 L19,7.5 A1.5,1.5 0 0 1 20.5,9 L20.5,15 A1.5,1.5 0 0 1 19,16.5 L5,16.5 "
        + "A1.5,1.5 0 0 1 3.5,15 L3.5,9 A1.5,1.5 0 0 1 5,7.5 Z "
        + "M7,12 L12.5,12 M15.4,11 L17,12.6 L18.6,11";

    /// <summary>A text field with an I-beam caret.</summary>
    private const string Text =
        "M5,7.5 L19,7.5 A1.5,1.5 0 0 1 20.5,9 L20.5,15 A1.5,1.5 0 0 1 19,16.5 L5,16.5 "
        + "A1.5,1.5 0 0 1 3.5,15 L3.5,9 A1.5,1.5 0 0 1 5,7.5 Z "
        + "M8.5,9.8 L8.5,14.2 M7.3,9.8 L9.7,9.8 M7.3,14.2 L9.7,14.2";

    private const string Thermometer =
        "M12,3.6 A2.4,2.4 0 0 1 14.4,6 L14.4,13.4 A4.2,4.2 0 1 1 9.6,13.4 L9.6,6 "
        + "A2.4,2.4 0 0 1 12,3.6 Z M12,9 L12,15";

    private const string Power =
        "M13.4,3 L6.6,13.2 L11.6,13.2 L10.6,21 L17.4,10.8 L12.4,10.8 Z";

    /// <summary>A cell standing upright, two bars of charge showing.</summary>
    private const string Battery =
        "M8.5,5 L15.5,5 A1.5,1.5 0 0 1 17,6.5 L17,19.5 A1.5,1.5 0 0 1 15.5,21 L8.5,21 "
        + "A1.5,1.5 0 0 1 7,19.5 L7,6.5 A1.5,1.5 0 0 1 8.5,5 Z "
        + "M10.5,3 L13.5,3 M10,17.6 L14,17.6 M10,14.6 L14,14.6";

    /// <summary>A dial with its needle on a hub.</summary>
    private const string Gauge =
        "M4.2,17.4 A9,9 0 1 1 19.8,17.4 M12.9,13.9 L16,9 "
        + "M12,13.4 A1.6,1.6 0 1 1 11.99,13.4 Z";

    private const string Motion =
        "M13.5,4.4 A1.7,1.7 0 1 1 13.49,4.4 Z "
        + "M13.5,8.4 L10.5,11.4 L10.5,15 L8,20 M10.5,11.4 L15.5,13.4 L17.5,17.4 "
        + "M10.5,11.4 L6.5,10.4";

    private const string Update =
        "M12,4.5 A7.5,7.5 0 1 0 19.5,12 M12,4.5 L12,13.5 M8.4,10 L12,13.5 L15.6,10";

    /// <summary>A ring with a centre point: something that is either on or off.</summary>
    private const string Dot =
        "M12,6.6 A5.4,5.4 0 1 1 11.99,6.6 Z M12,12 L12.01,12";

    /// <summary>The fallback: a plain rounded square, so an unknown domain still looks deliberate.</summary>
    private const string Generic =
        "M6.4,4.6 L17.6,4.6 A1.8,1.8 0 0 1 19.4,6.4 L19.4,17.6 A1.8,1.8 0 0 1 17.6,19.4 "
        + "L6.4,19.4 A1.8,1.8 0 0 1 4.6,17.6 L4.6,6.4 A1.8,1.8 0 0 1 6.4,4.6 Z";
}
