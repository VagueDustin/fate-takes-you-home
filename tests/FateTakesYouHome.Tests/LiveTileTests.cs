// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.Models;
using FateTakesYouHome.Services;
using FateTakesYouHome.ViewModels;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// Tiles stay true to the house: state events reach every tile that shows an entity, and a guess
/// made on a click never outlives the truth.
/// </summary>
/// <remarks>
/// Driven end to end through the fake server, on a thread with a running dispatcher, because that
/// is where the app's view models live: the service marshals its events onto that dispatcher and
/// the tiles' timers tick on it.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class LiveTileTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _root = Directory.CreateTempSubdirectory("fate-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A straggling handle on a temp directory is not worth failing the run over.
        }
    }

    // ------------------------------------------------------------------ layout widgets

    [Fact]
    public Task ATileInTheHomeLayoutFollowsTheEntity() => OnUiThread(async () =>
    {
        await using var server = new FakeHomeAssistantServer(Token) { States = [Lock("locked")] };
        using var log = new AppLog(Path.Combine(_root, "logs"));
        using var settings = new SettingsService(log, Path.Combine(_root, "settings.json"));
        settings.Current.HomeWidgets = [new WidgetSpec { Kind = WidgetKind.Tile, EntityId = FrontDoor }];
        HomeAssistantService homeAssistant = await ConnectAsync(server, log);

        // The dashboard only holds on to the tray controller; nothing here reaches it.
        using var dashboard = new DashboardViewModel(settings, homeAssistant, tray: null!);
        EntityTileViewModel tile = Assert.IsType<TileWidgetViewModel>(Assert.Single(dashboard.Widgets)).Tile;

        await PushAndWaitAsync(server, homeAssistant, Lock("unlocked"));

        Assert.Equal("Unlocked", tile.StateText);
        await homeAssistant.DisposeAsync();
    });

    /// <summary>
    /// The dangerous half of the stale-tile bug: a toggle picks its direction from the state it
    /// last heard, so a button frozen at "locked" answers a press on an unlocked door by sending
    /// unlock again instead of locking it.
    /// </summary>
    [Fact]
    public Task AQuickActionOnThePanelTogglesFromTheCurrentState() => OnUiThread(async () =>
    {
        await using var server = new FakeHomeAssistantServer(Token) { States = [Lock("locked")] };
        using var log = new AppLog(Path.Combine(_root, "logs"));
        using var settings = new SettingsService(log, Path.Combine(_root, "settings.json"));
        settings.Current.FlyoutWidgets = [new WidgetSpec { Kind = WidgetKind.QuickAction, EntityId = FrontDoor }];
        HomeAssistantService homeAssistant = await ConnectAsync(server, log);

        using var flyout = new FlyoutViewModel(settings, homeAssistant);
        var button = Assert.IsType<QuickActionWidgetViewModel>(Assert.Single(flyout.Widgets));

        await PushAndWaitAsync(server, homeAssistant, Lock("unlocked"));
        await button.ExecuteCommand.ExecuteAsync(null);

        JsonNode call = await server.WaitForCommandAsync("call_service", Patience);
        Assert.Equal("lock", call["service"]?.GetValue<string>());
        await homeAssistant.DisposeAsync();
    });

    // ------------------------------------------------------------------ optimistic state

    [Fact]
    public Task ARefusedToggleShowsTheRealStateAtOnce() => OnUiThread(async () =>
    {
        await using var server = new FakeHomeAssistantServer(Token)
        {
            States = [Lock("locked")],
            RefuseServiceCallsWith = "The lock is jammed.",
        };
        using var log = new AppLog(Path.Combine(_root, "logs"));
        using var settings = new SettingsService(log, Path.Combine(_root, "settings.json"));
        settings.Current.Pinned = [new PinnedEntity { EntityId = FrontDoor }];
        HomeAssistantService homeAssistant = await ConnectAsync(server, log);

        using var flyout = new FlyoutViewModel(settings, homeAssistant);
        EntityTileViewModel tile = Assert.Single(flyout.Tiles);

        await tile.PrimaryCommand.ExecuteAsync(null);

        Assert.Equal("The lock is jammed.", tile.ErrorMessage);
        Assert.False(tile.IsOn);
        Assert.Equal("Locked", tile.StateText);
        await homeAssistant.DisposeAsync();
    });

    /// <summary>
    /// The guess made on a click is only a stand-in for the event that confirms it. When the event
    /// says otherwise, the event wins, even inside the window the guess was given.
    /// </summary>
    [Fact]
    public Task AStateEventOverridesTheGuessMadeOnAClick() => OnUiThread(async () =>
    {
        await using var server = new FakeHomeAssistantServer(Token) { States = [Lock("locked")] };
        using var log = new AppLog(Path.Combine(_root, "logs"));
        using var settings = new SettingsService(log, Path.Combine(_root, "settings.json"));
        settings.Current.Pinned = [new PinnedEntity { EntityId = FrontDoor }];
        HomeAssistantService homeAssistant = await ConnectAsync(server, log);

        using var flyout = new FlyoutViewModel(settings, homeAssistant);
        EntityTileViewModel tile = Assert.Single(flyout.Tiles);

        await tile.PrimaryCommand.ExecuteAsync(null);
        Assert.Equal("Unlocked", tile.StateText);

        // Accepted, but the bolt never moved.
        await PushAndWaitAsync(server, homeAssistant, Lock("locked"));

        Assert.False(tile.IsOn);
        Assert.Equal("Locked", tile.StateText);
        await homeAssistant.DisposeAsync();
    });

    // ------------------------------------------------------------------ busy

    /// <summary>
    /// The control's enabled state is bound to <see cref="EntityTileViewModel.IsInteractive"/>, so
    /// it has to hear about the command starting and finishing, or a second press slips through.
    /// </summary>
    [Fact]
    public Task ATileIsNotInteractiveWhileItsCommandIsInFlight() => OnUiThread(async () =>
    {
        await using var server = new FakeHomeAssistantServer(Token) { States = [Entity(Blind, "closed")] };
        using var log = new AppLog(Path.Combine(_root, "logs"));
        using var settings = new SettingsService(log, Path.Combine(_root, "settings.json"));
        settings.Current.Pinned = [new PinnedEntity { EntityId = Blind }];
        HomeAssistantService homeAssistant = await ConnectAsync(server, log);

        using var flyout = new FlyoutViewModel(settings, homeAssistant);
        EntityTileViewModel tile = Assert.Single(flyout.Tiles);

        var heard = new List<bool>();
        tile.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EntityTileViewModel.IsInteractive))
            {
                heard.Add(tile.IsInteractive);
            }
        };

        await tile.OpenCommand.ExecuteAsync(null);

        Assert.Equal([false, true], heard);
        await homeAssistant.DisposeAsync();
    });

    // ------------------------------------------------------------------ slider

    /// <summary>
    /// A state event that lands between the last drag frame and the debounced send used to replace
    /// the user's value with the server's, and then send the server's value back to it.
    /// </summary>
    [Fact]
    public Task AStateEventMidDragDoesNotOverwriteTheSlider() => OnUiThread(async () =>
    {
        await using var server = new FakeHomeAssistantServer(Token) { States = [DeskLight(255)] };
        using var log = new AppLog(Path.Combine(_root, "logs"));
        using var settings = new SettingsService(log, Path.Combine(_root, "settings.json"));
        settings.Current.Pinned = [new PinnedEntity { EntityId = DeskLamp }];
        HomeAssistantService homeAssistant = await ConnectAsync(server, log);

        using var flyout = new FlyoutViewModel(settings, homeAssistant);
        EntityTileViewModel tile = Assert.Single(flyout.Tiles);

        tile.SliderValue = 40;
        await PushAndWaitAsync(server, homeAssistant, DeskLight(128));

        Assert.Equal(40, tile.SliderValue);

        JsonNode call = await server.WaitForCommandAsync("call_service", Patience);
        Assert.Equal(40, call["service_data"]?["brightness_pct"]?.GetValue<int>());
        await homeAssistant.DisposeAsync();
    });

    // ------------------------------------------------------------------ plumbing

    private const string FrontDoor = "lock.front_door";

    private const string Blind = "cover.study_blind";

    private const string DeskLamp = "light.desk_lamp";

    /// <summary>Long enough to pass the options' sanity check, which a real token always is.</summary>
    private const string Token = "fake-long-lived-access-token-for-tests";

    private static JsonObject Lock(string state) => Entity(FrontDoor, state);

    private static JsonObject DeskLight(int brightness) => Entity(
        DeskLamp,
        "on",
        new JsonObject { ["supported_color_modes"] = new JsonArray("brightness"), ["brightness"] = brightness });

    private static JsonObject Entity(string entityId, string state, JsonObject? attributes = null) => new()
    {
        ["entity_id"] = entityId,
        ["state"] = state,
        ["attributes"] = attributes ?? new JsonObject(),
    };

    private static async Task<HomeAssistantService> ConnectAsync(FakeHomeAssistantServer server, AppLog log)
    {
        var homeAssistant = new HomeAssistantService(log, Dispatcher.CurrentDispatcher);

        await homeAssistant.ApplyConnectionAsync(new HaConnectionOptions
        {
            BaseUrl = server.BaseUrl,
            AccessToken = server.ExpectedToken,
            CommandTimeout = TimeSpan.FromSeconds(5),
        });

        Assert.True(
            await FakeHomeAssistantServer.WaitUntilAsync(
                () => homeAssistant.IsReady && homeAssistant.StateCount > 0, Patience),
            $"Never became ready; ended at {homeAssistant.ConnectionState}: {homeAssistant.StatusMessage}");

        return homeAssistant;
    }

    /// <summary>Pushes a state change and returns once every view model has heard it.</summary>
    /// <remarks>
    /// Handlers run in subscription order, so one subscribed after the view models fires last.
    /// Waiting on it rather than on the tile is what makes a missed update fail at once instead
    /// of timing out.
    /// </remarks>
    private static async Task PushAndWaitAsync(
        FakeHomeAssistantServer server, HomeAssistantService homeAssistant, JsonObject state)
    {
        var heard = new TaskCompletionSource();
        void OnChanged(object? sender, EntityChangedEventArgs e) => heard.TrySetResult();

        homeAssistant.EntityChanged += OnChanged;
        await server.PushStateChangedAsync(state["entity_id"]!.GetValue<string>(), state);
        await heard.Task.WaitAsync(Patience);
        homeAssistant.EntityChanged -= OnChanged;
    }

    /// <summary>Runs <paramref name="body"/> on a thread of its own with a running dispatcher.</summary>
    private static Task OnUiThread(Func<Task> body)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await body();
                    done.SetResult();
                }
                catch (Exception ex)
                {
                    done.SetException(ex);
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
