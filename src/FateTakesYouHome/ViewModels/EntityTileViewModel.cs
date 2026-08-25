// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FateTakesYouHome.Controls;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.HomeAssistant.Models;
using FateTakesYouHome.Services;

namespace FateTakesYouHome.ViewModels;

/// <summary>What kind of control an entity needs.</summary>
public enum TileKind
{
    /// <summary>Has an on and an off. Gets a toggle.</summary>
    Toggle,

    /// <summary>Firing it is the whole interaction. Scenes, scripts, buttons.</summary>
    Momentary,

    /// <summary>Opens and closes over time. Covers and valves.</summary>
    Positional,

    /// <summary>Nothing to press. Sensors and trackers.</summary>
    Readout,
}

/// <summary>
/// One controllable thing, ready to bind.
/// </summary>
/// <remarks>
/// <para>
/// The tile is optimistic where it can afford to be. Home Assistant confirms a toggle within a few
/// tens of milliseconds on a LAN, but the round trip is still visible, so the tile shows the state
/// it expects and lets the real event overwrite it. If the call fails, the tile snaps back and
/// says why.
/// </para>
/// <para>
/// Slider edits are the other way round: they are debounced before being sent, because dragging
/// produces one change per frame and each would be a separate service call.
/// </para>
/// </remarks>
public sealed partial class EntityTileViewModel : ObservableObject
{
    /// <summary>How long a slider must be still before the change is sent.</summary>
    private static readonly TimeSpan SliderDebounce = TimeSpan.FromMilliseconds(140);

    /// <summary>
    /// How long after sending a slider value to ignore incoming updates for it.
    /// </summary>
    /// <remarks>
    /// Home Assistant echoes intermediate values back while a light ramps. Without this the thumb
    /// jumps backwards under the user's finger.
    /// </remarks>
    private static readonly TimeSpan EchoSuppression = TimeSpan.FromMilliseconds(1200);

    private readonly HomeAssistantService _service;
    private readonly DispatcherTimer _sliderTimer;

    private HaEntityState _state;
    private DateTimeOffset _suppressEchoUntil = DateTimeOffset.MinValue;
    private double _pendingSliderValue;
    private bool _updatingFromServer;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public EntityTileViewModel(HaEntityState state, HomeAssistantService service, string? labelOverride = null)
    {
        _state = state;
        _service = service;
        LabelOverride = labelOverride;

        _sliderTimer = new DispatcherTimer { Interval = SliderDebounce };
        _sliderTimer.Tick += OnSliderSettled;

        Glyph = EntityGlyphs.For(state);
    }

    public string EntityId => _state.EntityId;

    public string Domain => _state.Domain;

    /// <summary>A name the user set for this pin, overriding the server's friendly name.</summary>
    public string? LabelOverride { get; set; }

    public Geometry Glyph { get; private set; }

    public string DisplayName => LabelOverride is { Length: > 0 } ? LabelOverride : _state.FriendlyName;

    /// <summary>Area name, or the domain when the entity has not been assigned to one.</summary>
    public string SubLabel
    {
        get
        {
            HaArea? area = _service.AreaFor(EntityId);

            if (area is not null)
            {
                return area.Name;
            }

            return HaEntityState.HumaniseObjectId(Domain);
        }
    }

    /// <summary>
    /// Whether the entity is on, honouring an in-flight optimistic change.
    /// </summary>
    /// <remarks>
    /// Reads through <see cref="EffectiveIsOn"/> rather than the raw state, so a toggle looks
    /// applied the moment it is clicked instead of one network round trip later.
    /// </remarks>
    public bool IsOn => EffectiveIsOn;

    public bool IsUnavailable => _state.IsUnavailable;

    /// <summary>Whether the primary control should be interactive.</summary>
    public bool IsInteractive => !IsUnavailable && Kind != TileKind.Readout && !IsBusy;

    public TileKind Kind => Domain switch
    {
        HaDomains.Scene or HaDomains.Button or HaDomains.InputButton => TileKind.Momentary,
        HaDomains.Script => TileKind.Momentary,
        HaDomains.Cover or HaDomains.Valve => TileKind.Positional,
        _ when HaDomains.ReadOnly.Contains(Domain) => TileKind.Readout,
        _ => TileKind.Toggle,
    };

    /// <summary>True when the entity carries a 0–100 value worth putting a slider on.</summary>
    public bool HasSlider => Domain switch
    {
        HaDomains.Light => SupportsBrightness,
        HaDomains.Fan => _state.Supports(HaFeatures.Fan.SetSpeed),
        HaDomains.Cover => _state.Supports(HaFeatures.Cover.SetPosition),
        HaDomains.Valve => _state.Supports(HaFeatures.Valve.SetPosition),
        HaDomains.MediaPlayer => _state.Supports(HaFeatures.MediaPlayer.VolumeSet),
        _ => false,
    };

    private bool SupportsBrightness =>
        _state.AttrStringList("supported_color_modes").Any(HaColorModes.HasBrightness);

    /// <summary>The slider's value, 0–100. Setting it schedules a debounced service call.</summary>
    public double SliderValue
    {
        get => ReadSliderValue();
        set
        {
            double clamped = Math.Clamp(value, 0, 100);

            if (Math.Abs(clamped - _pendingSliderValue) < 0.5 && !_updatingFromServer)
            {
                return;
            }

            _pendingSliderValue = clamped;
            OnPropertyChanged();

            if (_updatingFromServer)
            {
                return;
            }

            // Restart the timer on every change: the call goes out once the drag stops.
            _sliderTimer.Stop();
            _sliderTimer.Start();
        }
    }

    /// <summary>The human-readable state, e.g. "On · 45%" or "21.5 °C".</summary>
    public string StateText => FormatState();

    /// <summary>Semantic status for colouring: null means neutral.</summary>
    public string? StatusKind => IsUnavailable ? "unavailable" : IsOn ? "active" : null;

    // ------------------------------------------------------------------ commands

    /// <summary>Toggles, activates or fires the entity, depending on what it is.</summary>
    [RelayCommand]
    private async Task PrimaryAsync()
    {
        if (!IsInteractive)
        {
            return;
        }

        // The state the user just asked for, shown immediately so the tile feels connected to the
        // click rather than to the network.
        bool optimistic = Kind == TileKind.Momentary || !IsOn;

        IsBusy = true;
        ErrorMessage = null;

        if (Kind != TileKind.Momentary)
        {
            ApplyOptimisticState(optimistic);
        }

        CommandResult result = await _service
            .ExecuteAsync(
                (client, ct) => Kind == TileKind.Momentary
                    ? client.TurnOnAsync(EntityId, ct)
                    : client.ToggleAsync(_state, ct),
                $"Primary action on {EntityId}")
            .ConfigureAwait(true);

        IsBusy = false;

        if (result.Succeeded)
        {
            return;
        }

        ErrorMessage = result.ErrorMessage;

        // The optimistic state was a guess and it was wrong. Put the real one back.
        RaiseStateProperties();
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (IsUnavailable)
        {
            return;
        }

        await RunAsync(
            (client, ct) => client.TurnOnAsync(EntityId, ct),
            $"Open {EntityId}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (IsUnavailable)
        {
            return;
        }

        await RunAsync(
            (client, ct) => client.TurnOffAsync(EntityId, ct),
            $"Close {EntityId}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (Domain is not (HaDomains.Cover or HaDomains.Valve))
        {
            return;
        }

        await RunAsync(
            (client, ct) => Domain == HaDomains.Cover
                ? client.StopCoverAsync(EntityId, ct)
                : client.CallServiceAsync(HaDomains.Valve, "stop_valve", EntityId, ct: ct),
            $"Stop {EntityId}").ConfigureAwait(true);
    }

    private async Task RunAsync(Func<HaClient, CancellationToken, Task> action, string description)
    {
        IsBusy = true;
        ErrorMessage = null;

        CommandResult result = await _service.ExecuteAsync(action, description).ConfigureAwait(true);

        IsBusy = false;

        if (!result.Succeeded)
        {
            ErrorMessage = result.ErrorMessage;
        }
    }

    // ------------------------------------------------------------------ state plumbing

    /// <summary>Feeds a new state in from the service.</summary>
    public void Update(HaEntityState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state;
        Glyph = EntityGlyphs.For(state);

        RaiseStateProperties();
    }

    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SubLabel));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(IsUnavailable));
        OnPropertyChanged(nameof(IsInteractive));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(HasSlider));

        // While the echo window is open the thumb belongs to the user, not to the server.
        if (DateTimeOffset.UtcNow >= _suppressEchoUntil)
        {
            _updatingFromServer = true;
            _pendingSliderValue = ReadSliderValue();
            OnPropertyChanged(nameof(SliderValue));
            _updatingFromServer = false;
        }
    }

    /// <summary>
    /// Shows the state the user asked for, before the server has confirmed it.
    /// </summary>
    /// <remarks>
    /// Only the derived flags are raised, not the underlying state object — the next real event
    /// overwrites this, and if the call fails the tile is refreshed from the truth.
    /// </remarks>
    private void ApplyOptimisticState(bool on)
    {
        _optimisticOn = on;
        _optimisticUntil = DateTimeOffset.UtcNow.AddSeconds(3);

        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StatusKind));
    }

    private bool _optimisticOn;
    private DateTimeOffset _optimisticUntil = DateTimeOffset.MinValue;

    private bool EffectiveIsOn =>
        DateTimeOffset.UtcNow < _optimisticUntil ? _optimisticOn : _state.IsOn;

    private double ReadSliderValue() => Domain switch
    {
        // Brightness is 0–255 on the wire; the slider speaks percent because that is what the
        // number under it says.
        HaDomains.Light => (_state.AttrDouble("brightness") ?? 0) / 255d * 100d,
        HaDomains.Fan => _state.AttrDouble("percentage") ?? 0,
        HaDomains.Cover => _state.AttrDouble("current_position") ?? 0,
        HaDomains.Valve => _state.AttrDouble("current_position") ?? 0,
        HaDomains.MediaPlayer => (_state.AttrDouble("volume_level") ?? 0) * 100d,
        _ => 0,
    };

    private async void OnSliderSettled(object? sender, EventArgs e)
    {
        _sliderTimer.Stop();

        double value = _pendingSliderValue;
        _suppressEchoUntil = DateTimeOffset.UtcNow.Add(EchoSuppression);

        CommandResult result = await _service
            .ExecuteAsync(
                (client, ct) => Domain switch
                {
                    HaDomains.Light => client.SetLightBrightnessAsync(EntityId, value, ct),
                    HaDomains.Fan => client.SetFanPercentageAsync(EntityId, value, ct),
                    HaDomains.Cover => client.SetCoverPositionAsync(EntityId, value, ct),
                    HaDomains.Valve => client.CallServiceAsync(
                        HaDomains.Valve,
                        "set_valve_position",
                        EntityId,
                        new Dictionary<string, object?> { ["position"] = (int)Math.Round(value) },
                        ct),
                    HaDomains.MediaPlayer => client.SetMediaVolumeAsync(EntityId, value, ct),
                    _ => Task.CompletedTask,
                },
                $"Set {EntityId} to {value:0}%")
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            ErrorMessage = result.ErrorMessage;

            // Let the server's value win again straight away, so the thumb returns to the truth.
            _suppressEchoUntil = DateTimeOffset.MinValue;
            RaiseStateProperties();
        }
    }

    // ------------------------------------------------------------------ formatting

    private string FormatState()
    {
        if (IsUnavailable)
        {
            return _state.State == "unknown" ? "Unknown" : "Unavailable";
        }

        return Domain switch
        {
            HaDomains.Light => FormatLight(),
            HaDomains.Cover or HaDomains.Valve => FormatPositional(),
            HaDomains.Climate => FormatClimate(),
            HaDomains.Fan => FormatFan(),
            HaDomains.MediaPlayer => FormatMedia(),
            HaDomains.Lock => EffectiveIsOn ? "Unlocked" : "Locked",
            HaDomains.Sensor => FormatMeasurement(),
            HaDomains.BinarySensor => FormatBinarySensor(),
            HaDomains.Automation => _state.State == "on" ? "Enabled" : "Disabled",
            HaDomains.Scene => "Scene",
            HaDomains.Script => _state.State == "on" ? "Running" : "Ready",
            HaDomains.Button or HaDomains.InputButton => "Press",
            HaDomains.Number or HaDomains.InputNumber => FormatMeasurement(),
            HaDomains.Select or HaDomains.InputSelect => HaEntityState.HumaniseObjectId(_state.State),
            HaDomains.Person or HaDomains.DeviceTracker => HaEntityState.HumaniseObjectId(_state.State),
            _ => EffectiveIsOn ? "On" : HaEntityState.HumaniseObjectId(_state.State),
        };
    }

    private string FormatLight()
    {
        if (!EffectiveIsOn)
        {
            return "Off";
        }

        double? brightness = _state.AttrDouble("brightness");

        if (brightness is null)
        {
            return "On";
        }

        int percent = (int)Math.Round(brightness.Value / 255d * 100d);
        return $"On · {percent}%";
    }

    private string FormatPositional()
    {
        double? position = _state.AttrDouble("current_position");
        string word = HaEntityState.HumaniseObjectId(_state.State);

        return position is null ? word : $"{word} · {position.Value:0}%";
    }

    private string FormatClimate()
    {
        string mode = HaEntityState.HumaniseObjectId(_state.State);
        double? target = _state.AttrDouble("temperature");
        double? current = _state.AttrDouble("current_temperature");

        if (target is null && current is null)
        {
            return mode;
        }

        string unit = TemperatureUnit();

        if (target is not null && current is not null)
        {
            return $"{mode} · {current.Value:0.#}{unit} → {target.Value:0.#}{unit}";
        }

        double value = target ?? current!.Value;
        return $"{mode} · {value:0.#}{unit}";
    }

    private string FormatFan()
    {
        if (!EffectiveIsOn)
        {
            return "Off";
        }

        double? percentage = _state.AttrDouble("percentage");
        string? preset = _state.AttrString("preset_mode");

        if (preset is { Length: > 0 })
        {
            return $"On · {preset}";
        }

        return percentage is null ? "On" : $"On · {percentage.Value:0}%";
    }

    private string FormatMedia()
    {
        string status = _state.State switch
        {
            "playing" => "Playing",
            "paused" => "Paused",
            "idle" => "Idle",
            "off" => "Off",
            "standby" => "Standby",
            _ => HaEntityState.HumaniseObjectId(_state.State),
        };

        string? title = _state.AttrString("media_title");
        string? artist = _state.AttrString("media_artist");

        if (title is not { Length: > 0 })
        {
            return status;
        }

        return artist is { Length: > 0 }
            ? $"{status} · {artist} — {title}"
            : $"{status} · {title}";
    }

    private string FormatMeasurement()
    {
        string? unit = _state.AttrString("unit_of_measurement");

        if (!double.TryParse(
                _state.State, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return HaEntityState.HumaniseObjectId(_state.State);
        }

        // Round to something a person would say out loud rather than to full sensor precision.
        string formatted = Math.Abs(value) >= 100
            ? value.ToString("0", CultureInfo.CurrentCulture)
            : value.ToString("0.#", CultureInfo.CurrentCulture);

        return unit is { Length: > 0 } ? $"{formatted} {unit}" : formatted;
    }

    private string FormatBinarySensor()
    {
        string? deviceClass = _state.AttrString("device_class");
        bool on = _state.State == "on";

        return deviceClass switch
        {
            "motion" or "occupancy" or "presence" => on ? "Detected" : "Clear",
            "door" or "window" or "opening" or "garage_door" => on ? "Open" : "Closed",
            "moisture" => on ? "Wet" : "Dry",
            "smoke" or "gas" or "carbon_monoxide" => on ? "Detected" : "Clear",
            "problem" => on ? "Problem" : "OK",
            "connectivity" => on ? "Connected" : "Disconnected",
            "battery" => on ? "Low" : "OK",
            _ => on ? "On" : "Off",
        };
    }

    private string TemperatureUnit()
    {
        // Climate entities report their own unit only sometimes; the degree sign alone is safer
        // than guessing wrong between C and F.
        string? unit = _state.AttrString("unit_of_measurement");
        return unit is { Length: > 0 } ? " " + unit : "°";
    }

    /// <summary>Stops the debounce timer. Called when a tile leaves the visual tree.</summary>
    public void Detach()
    {
        _sliderTimer.Stop();
        _sliderTimer.Tick -= OnSliderSettled;
    }
}
