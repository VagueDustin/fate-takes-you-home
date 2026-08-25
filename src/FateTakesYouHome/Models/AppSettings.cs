// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace FateTakesYouHome.Models;

/// <summary>What a tray gesture does.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrayAction>))]
public enum TrayAction
{
    /// <summary>Do nothing at all.</summary>
    None,

    /// <summary>Show or hide the tray flyout.</summary>
    ToggleFlyout,

    /// <summary>Open the full window.</summary>
    OpenMainWindow,

    /// <summary>Fire whichever pinned item is marked as the default action.</summary>
    RunDefaultAction,
}

/// <summary>How entities are grouped on the full entity browser.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EntityGrouping>))]
public enum EntityGrouping
{
    Area,
    Domain,
    Floor,
    None,
}

/// <summary>The user's font choices, overriding the theme's. Null means "whatever the theme says".</summary>
public sealed class FontOverrides
{
    [JsonPropertyName("display")]
    public string? Display { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("mono")]
    public string? Mono { get; set; }

    [JsonIgnore]
    public bool Any => Display is not null || Body is not null || Mono is not null;
}

/// <summary>One item the user pinned to the tray flyout.</summary>
public sealed class PinnedEntity
{
    [JsonPropertyName("entityId")]
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Overrides the friendly name from Home Assistant. Null keeps the server's name.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>
    /// Fired by the "run default action" tray gesture. At most one pin should have this set;
    /// the service enforces it on save.
    /// </summary>
    [JsonPropertyName("isDefaultAction")]
    public bool IsDefaultAction { get; set; }

    /// <summary>Ask before firing. For the pins somebody would rather not hit by accident.</summary>
    [JsonPropertyName("confirmBeforeRunning")]
    public bool ConfirmBeforeRunning { get; set; }
}

/// <summary>
/// Everything the application persists.
/// </summary>
/// <remarks>
/// The access token is never held here in plain text — <see cref="ProtectedToken"/> is a DPAPI
/// blob. See <see cref="Services.SecretProtector"/>.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>Bumped when a migration is needed. Read by the settings service on load.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    // -- Connection -------------------------------------------------------------------------

    /// <summary>Base URL of the Home Assistant instance, e.g. <c>http://homeassistant.local:8123</c>.</summary>
    [JsonPropertyName("serverUrl")]
    public string? ServerUrl { get; set; }

    /// <summary>The Long-Lived Access Token, encrypted with DPAPI for the current user.</summary>
    [JsonPropertyName("protectedToken")]
    public string? ProtectedToken { get; set; }

    /// <summary>Accept a TLS certificate that does not chain to a trusted root.</summary>
    [JsonPropertyName("allowInvalidCertificate")]
    public bool AllowInvalidCertificate { get; set; }

    // -- Appearance -------------------------------------------------------------------------

    [JsonPropertyName("themeId")]
    public string ThemeId { get; set; } = "fate";

    /// <summary>
    /// Suppress every animation regardless of what the theme asks for.
    /// </summary>
    /// <remarks>
    /// Separate from the theme's own motion switch so that turning animations off is not lost the
    /// moment somebody tries a different theme.
    /// </remarks>
    [JsonPropertyName("disableAnimations")]
    public bool DisableAnimations { get; set; }

    // -- Behaviour --------------------------------------------------------------------------

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    [JsonPropertyName("trayDoubleClickAction")]
    public TrayAction TrayDoubleClickAction { get; set; } = TrayAction.OpenMainWindow;

    [JsonPropertyName("trayMiddleClickAction")]
    public TrayAction TrayMiddleClickAction { get; set; } = TrayAction.RunDefaultAction;

    /// <summary>Closing the full window hides it rather than exiting the application.</summary>
    [JsonPropertyName("closeToTray")]
    public bool CloseToTray { get; set; } = true;

    /// <summary>Keep the flyout open when it loses focus. Useful while arranging pins.</summary>
    [JsonPropertyName("pinFlyoutOpen")]
    public bool PinFlyoutOpen { get; set; }

    // -- Content ----------------------------------------------------------------------------

    [JsonPropertyName("pinned")]
    public List<PinnedEntity> Pinned { get; set; } = [];

    [JsonPropertyName("grouping")]
    public EntityGrouping Grouping { get; set; } = EntityGrouping.Area;

    /// <summary>Show entities Home Assistant currently reports as unavailable.</summary>
    [JsonPropertyName("showUnavailable")]
    public bool ShowUnavailable { get; set; } = true;

    /// <summary>Show config and diagnostic entities in the browser. Off by default; they are noise.</summary>
    [JsonPropertyName("showAuxiliaryEntities")]
    public bool ShowAuxiliaryEntities { get; set; }

    // -- First run --------------------------------------------------------------------------

    [JsonPropertyName("hasCompletedOnboarding")]
    public bool HasCompletedOnboarding { get; set; }

    /// <summary>The version that last ran, so an upgrade can show what changed.</summary>
    [JsonPropertyName("lastRunVersion")]
    public string? LastRunVersion { get; set; }

    // -- Diagnostics ------------------------------------------------------------------------

    /// <summary>Write debug-level entries to the log file.</summary>
    [JsonPropertyName("verboseLogging")]
    public bool VerboseLogging { get; set; }

    /// <summary>Whether to look at GitHub releases for a newer version once a day.</summary>
    [JsonPropertyName("checkForUpdates")]
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>System-wide shortcuts, keyed by action name, as "Ctrl+Alt+H" text. Null = unset.</summary>
    [JsonPropertyName("shortcuts")]
    public Dictionary<string, string?> Shortcuts { get; set; } = [];

    /// <summary>
    /// Font families the user chose over whatever the theme asks for. Null entries defer to the
    /// theme. Kept as a setting rather than a theme so a font choice survives switching themes.
    /// </summary>
    [JsonPropertyName("fontOverrides")]
    public FontOverrides FontOverrides { get; set; } = new();

    /// <summary>The home screen's widget layout. Null means the standard pinned-and-rooms view.</summary>
    [JsonPropertyName("homeWidgets")]
    public List<WidgetSpec>? HomeWidgets { get; set; }

    /// <summary>The tray panel's widget layout. Null means the standard pinned list.</summary>
    [JsonPropertyName("flyoutWidgets")]
    public List<WidgetSpec>? FlyoutWidgets { get; set; }

    /// <summary>Deep copy, used so the settings page can edit without committing.</summary>
    public AppSettings Clone() => new()
    {
        Version = Version,
        ServerUrl = ServerUrl,
        ProtectedToken = ProtectedToken,
        AllowInvalidCertificate = AllowInvalidCertificate,
        ThemeId = ThemeId,
        DisableAnimations = DisableAnimations,
        StartWithWindows = StartWithWindows,
        TrayDoubleClickAction = TrayDoubleClickAction,
        TrayMiddleClickAction = TrayMiddleClickAction,
        CloseToTray = CloseToTray,
        PinFlyoutOpen = PinFlyoutOpen,
        Pinned = Pinned
            .Select(p => new PinnedEntity
            {
                EntityId = p.EntityId,
                Label = p.Label,
                IsDefaultAction = p.IsDefaultAction,
                ConfirmBeforeRunning = p.ConfirmBeforeRunning,
            })
            .ToList(),
        Grouping = Grouping,
        ShowUnavailable = ShowUnavailable,
        ShowAuxiliaryEntities = ShowAuxiliaryEntities,
        HasCompletedOnboarding = HasCompletedOnboarding,
        LastRunVersion = LastRunVersion,
        VerboseLogging = VerboseLogging,
        CheckForUpdates = CheckForUpdates,
        Shortcuts = new Dictionary<string, string?>(Shortcuts),
        HomeWidgets = HomeWidgets?.Select(w => w.Clone()).ToList(),
        FlyoutWidgets = FlyoutWidgets?.Select(w => w.Clone()).ToList(),
        FontOverrides = new FontOverrides
        {
            Display = FontOverrides.Display,
            Body = FontOverrides.Body,
            Mono = FontOverrides.Mono,
        },
    };

    /// <summary>True when there is enough here to attempt a connection.</summary>
    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(ProtectedToken);
}
