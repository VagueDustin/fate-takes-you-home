namespace FateTakesYouHome.HomeAssistant.Models;

/// <summary>
/// Entity domain names, and which ones this app renders a purpose-built control for.
/// </summary>
public static class HaDomains
{
    public const string Light = "light";
    public const string Switch = "switch";
    public const string InputBoolean = "input_boolean";
    public const string Scene = "scene";
    public const string Script = "script";
    public const string Automation = "automation";
    public const string Cover = "cover";
    public const string Climate = "climate";
    public const string Fan = "fan";
    public const string Lock = "lock";
    public const string MediaPlayer = "media_player";
    public const string Sensor = "sensor";
    public const string BinarySensor = "binary_sensor";
    public const string Button = "button";
    public const string InputButton = "input_button";
    public const string Vacuum = "vacuum";
    public const string Humidifier = "humidifier";
    public const string WaterHeater = "water_heater";
    public const string Camera = "camera";
    public const string Person = "person";
    public const string DeviceTracker = "device_tracker";
    public const string Number = "number";
    public const string InputNumber = "input_number";
    public const string Select = "select";
    public const string InputSelect = "input_select";
    public const string Text = "text";
    public const string InputText = "input_text";
    public const string Siren = "siren";
    public const string Valve = "valve";
    public const string Update = "update";
    public const string Todo = "todo";

    /// <summary>Domains with a hand-tuned control in this release.</summary>
    public static readonly IReadOnlySet<string> FirstClass = new HashSet<string>(StringComparer.Ordinal)
    {
        Light, Switch, InputBoolean, Scene, Script, Automation, Cover, Climate,
        Fan, Lock, MediaPlayer, Button, InputButton, Number, InputNumber,
        Select, InputSelect, Siren, Valve, Humidifier, Vacuum, WaterHeater,
    };

    /// <summary>Domains rendered as read-only readouts rather than controls.</summary>
    public static readonly IReadOnlySet<string> ReadOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        Sensor, BinarySensor, Person, DeviceTracker, Camera, Update, Todo,
    };

    /// <summary>Domains that are momentary — activating them has no "off".</summary>
    public static readonly IReadOnlySet<string> Momentary = new HashSet<string>(StringComparer.Ordinal)
    {
        Scene, Button, InputButton,
    };

    /// <summary>Domains that respond to <c>homeassistant.toggle</c>.</summary>
    public static readonly IReadOnlySet<string> Toggleable = new HashSet<string>(StringComparer.Ordinal)
    {
        Light, Switch, InputBoolean, Fan, Siren, Humidifier, Automation,
    };

    public static bool IsSupported(string domain) =>
        FirstClass.Contains(domain) || ReadOnly.Contains(domain);
}

/// <summary>
/// <c>supported_features</c> bitmasks, mirroring Home Assistant's per-domain feature enums.
/// </summary>
public static class HaFeatures
{
    public static class Light
    {
        public const int Effect = 4;
        public const int Flash = 8;
        public const int Transition = 32;
    }

    public static class Cover
    {
        public const int Open = 1;
        public const int Close = 2;
        public const int SetPosition = 4;
        public const int Stop = 8;
        public const int OpenTilt = 16;
        public const int CloseTilt = 32;
        public const int StopTilt = 64;
        public const int SetTiltPosition = 128;
    }

    public static class Climate
    {
        public const int TargetTemperature = 1;
        public const int TargetTemperatureRange = 2;
        public const int TargetHumidity = 4;
        public const int FanMode = 8;
        public const int PresetMode = 16;
        public const int SwingMode = 32;
        public const int AuxHeat = 64;
        public const int TurnOff = 128;
        public const int TurnOn = 256;
    }

    public static class Fan
    {
        public const int SetSpeed = 1;
        public const int Oscillate = 2;
        public const int Direction = 4;
        public const int PresetMode = 8;
        public const int TurnOff = 16;
        public const int TurnOn = 32;
    }

    public static class MediaPlayer
    {
        public const int Pause = 1;
        public const int Seek = 2;
        public const int VolumeSet = 4;
        public const int VolumeMute = 8;
        public const int PreviousTrack = 16;
        public const int NextTrack = 32;
        public const int TurnOn = 128;
        public const int TurnOff = 256;
        public const int PlayMedia = 512;
        public const int VolumeStep = 1024;
        public const int SelectSource = 2048;
        public const int Stop = 4096;
        public const int Play = 16384;
        public const int Shuffle = 32768;
        public const int SelectSoundMode = 65536;
        public const int Repeat = 262144;
    }

    public static class Lock
    {
        public const int Open = 1;
    }

    public static class Vacuum
    {
        public const int Start = 8192;
        public const int Pause = 4;
        public const int Stop = 8;
        public const int ReturnHome = 16;
        public const int FanSpeed = 32;
        public const int Locate = 512;
        public const int Clean = 1024;
    }

    public static class Siren
    {
        public const int TurnOn = 1;
        public const int TurnOff = 2;
        public const int Tones = 4;
        public const int Volume = 8;
        public const int Duration = 16;
    }

    public static class Valve
    {
        public const int Open = 1;
        public const int Close = 2;
        public const int SetPosition = 4;
        public const int Stop = 8;
    }

    public static class Humidifier
    {
        public const int Modes = 1;
    }
}

/// <summary>Light colour modes as reported in <c>supported_color_modes</c>.</summary>
public static class HaColorModes
{
    public const string OnOff = "onoff";
    public const string Brightness = "brightness";
    public const string ColorTemp = "color_temp";
    public const string Hs = "hs";
    public const string Xy = "xy";
    public const string Rgb = "rgb";
    public const string Rgbw = "rgbw";
    public const string Rgbww = "rgbww";
    public const string White = "white";

    /// <summary>Colour modes that let the user pick an arbitrary hue.</summary>
    public static bool IsFullColour(string mode) =>
        mode is Hs or Xy or Rgb or Rgbw or Rgbww;

    /// <summary>Colour modes that carry a brightness channel.</summary>
    public static bool HasBrightness(string mode) =>
        mode is not (OnOff or "unknown" or "");
}
