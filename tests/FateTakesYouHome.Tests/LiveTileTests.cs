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

    // ------------------------------------------------------------------ plumbing

    private const string FrontDoor = "lock.front_door";

    /// <summary>Long enough to pass the options' sanity check, which a real token always is.</summary>
    private const string Token = "fake-long-lived-access-token-for-tests";

    private static JsonObject Lock(string state) => new()
    {
        ["entity_id"] = FrontDoor,
        ["state"] = state,
        ["attributes"] = new JsonObject { ["friendly_name"] = "Front Door" },
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
        await server.PushStateChangedAsync(FrontDoor, state);
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
