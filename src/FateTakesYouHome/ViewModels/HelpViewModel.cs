// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FateTakesYouHome.Services;

namespace FateTakesYouHome.ViewModels;

/// <summary>One numbered step in the quickstart.</summary>
public sealed partial class QuickstartStep : ObservableObject
{
    [ObservableProperty]
    private bool _isComplete;

    public required int Number { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    /// <summary>Label for the step's button, or null when there is nothing to click.</summary>
    public string? ActionLabel { get; init; }

    public IRelayCommand? Action { get; init; }
}

/// <summary>A question somebody will actually ask, and the answer.</summary>
public sealed record HelpTopic(string Question, string Answer);

/// <summary>
/// The quickstart, the guided tour trigger, and troubleshooting.
/// </summary>
/// <remarks>
/// The quickstart tracks real state rather than a checklist the user ticks off themselves: a step
/// is complete when the thing it asks for has actually happened. Somebody returning after a
/// failed setup can see at a glance which part did not take.
/// </remarks>
public sealed partial class HelpViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly TrayController _tray;

    public HelpViewModel(SettingsService settings, TrayController tray)
    {
        _settings = settings;
        _tray = tray;

        Steps =
        [
            new QuickstartStep
            {
                Number = 1,
                Title = "Create a Long-Lived Access Token",
                Body = "In Home Assistant, open your profile, scroll to the bottom of the Security "
                     + "tab, and create a Long-Lived Access Token. Copy it — Home Assistant will "
                     + "not show it again.",
                ActionLabel = "Open Home Assistant",
                Action = new RelayCommand(OpenHomeAssistant),
            },
            new QuickstartStep
            {
                Number = 2,
                Title = "Paste it into Settings",
                Body = "Enter your server address and the token, then use Test connection to "
                     + "confirm the two agree before saving.",
                ActionLabel = "Go to Settings",
                Action = new RelayCommand(() => tray.ShowMainWindow(MainWindowSection.Settings)),
            },
            new QuickstartStep
            {
                Number = 3,
                Title = "Pin what you reach for",
                Body = "Browse everything Home Assistant knows about and pin the lights, scenes and "
                     + "automations you use daily. Pinned items are what the tray panel shows.",
                ActionLabel = "Browse entities",
                Action = new RelayCommand(() => tray.ShowMainWindow(MainWindowSection.Entities)),
            },
            new QuickstartStep
            {
                Number = 4,
                Title = "Make it start with Windows",
                Body = "A tray app is only useful if it is there. Turn on Start with Windows and it "
                     + "will be waiting after every reboot.",
                ActionLabel = "Go to Settings",
                Action = new RelayCommand(() => tray.ShowMainWindow(MainWindowSection.Settings)),
            },
        ];

        Topics =
        [
            new HelpTopic(
                "The tray icon is not showing up.",
                "Windows hides new tray icons by default. Click the chevron next to the clock, or "
                + "open Settings → Personalisation → Taskbar → Other system tray icons, and switch "
                + "Fate Takes You Home on."),

            new HelpTopic(
                "It says the token was rejected.",
                "Long-Lived Access Tokens are tied to the Home Assistant user that created them. "
                + "If that user was deleted or the token revoked, create a new one. Tokens do not "
                + "expire on their own, so a token that stopped working was revoked somewhere."),

            new HelpTopic(
                "It cannot reach my server.",
                "Check the address in a browser from this same PC first. Use the port Home "
                + "Assistant actually listens on — 8123 unless you changed it — and include https "
                + "only if you have TLS set up. If you use a self-signed certificate, turn on "
                + "Accept self-signed certificates."),

            new HelpTopic(
                "The connection keeps dropping.",
                "The app reconnects on its own with a widening delay, so brief outages heal "
                + "themselves. Persistent drops usually mean a reverse proxy timing out idle "
                + "WebSocket connections — raise the proxy's read timeout above 60 seconds."),

            new HelpTopic(
                "An entity is missing from the browser.",
                "Disabled and hidden entities are filtered out, and so are config and diagnostic "
                + "entities. Turn on Show diagnostic entities to see the last group. Entities that "
                + "are unavailable are hidden if you switched that filter off."),

            new HelpTopic(
                "Where are my settings and themes kept?",
                "Settings and themes live in %APPDATA%\\VagueDustin Enterprises\\Fate Takes You "
                + "Home. Logs are under %LOCALAPPDATA%. The access token is encrypted with Windows "
                + "data protection and is readable only by your Windows account."),

            new HelpTopic(
                "Can I write my own theme?",
                "Yes. Drop a .json file in the themes folder and it appears in the picker within a "
                + "second; edit it while the app runs and the interface repaints as you save. A "
                + "theme only has to declare what it changes. The Themes page has an editor with "
                + "live preview if you would rather not write JSON by hand."),

            new HelpTopic(
                "How do I stop the animations?",
                "Settings → Appearance → Disable animations. The app also honours the Windows "
                + "animation setting under Accessibility → Visual effects, so turning animations "
                + "off system-wide switches them off here too."),
        ];

        Refresh();
    }

    public ObservableCollection<QuickstartStep> Steps { get; }

    public IReadOnlyList<HelpTopic> Topics { get; }

    /// <summary>Raised when the user asks for the guided tour.</summary>
    public event EventHandler? TourRequested;

    public static string VersionLine => $"Fate Takes You Home {App.DisplayVersion}";

    public static string InstallLine => AppPaths.InstallDirectory;

    public static string DataLine => AppPaths.RoamingData;

    public static string LogLine => AppPaths.LogsDirectory;

    public int CompletedCount => Steps.Count(s => s.IsComplete);

    public string ProgressLine => $"{CompletedCount} of {Steps.Count} done";

    public bool IsSetupComplete => CompletedCount == Steps.Count;

    /// <summary>Recomputes which steps are actually done.</summary>
    public void Refresh()
    {
        bool hasToken = !string.IsNullOrWhiteSpace(_settings.Current.ProtectedToken);
        bool hasServer = !string.IsNullOrWhiteSpace(_settings.Current.ServerUrl);

        Steps[0].IsComplete = hasToken;
        Steps[1].IsComplete = hasToken && hasServer;
        Steps[2].IsComplete = _settings.Current.Pinned.Count > 0;
        Steps[3].IsComplete = App.Current?.Autostart.IsEnabled ?? false;

        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(ProgressLine));
        OnPropertyChanged(nameof(IsSetupComplete));
    }

    [RelayCommand]
    private void StartTour() => TourRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenHomeAssistant()
    {
        string? url = _settings.Current.ServerUrl;

        // Send them to their own instance's profile page when we know where it is; otherwise to
        // the documentation, which explains what a Long-Lived Access Token is.
        string target = string.IsNullOrWhiteSpace(url)
            ? "https://www.home-assistant.io/docs/authentication/#your-account-profile"
            : url.TrimEnd('/') + "/profile/security";

        OpenUrl(target);
    }

    [RelayCommand]
    private void OpenLogFile()
    {
        try
        {
            string? path = App.Current?.Log.FilePath;

            if (path is not null && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }

            Process.Start(new ProcessStartInfo(AppPaths.LogsDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Current?.Log.Warning("Could not open the log.", ex);
        }
    }

    [RelayCommand]
    private void OpenProjectPage() =>
        OpenUrl("https://github.com/VagueDustin/fate-takes-you-home");

    [RelayCommand]
    private void OpenHomeAssistantDocs() =>
        OpenUrl("https://www.home-assistant.io/docs/");

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Current?.Log.Warning($"Could not open '{url}'.", ex);
        }
    }
}
