// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.Services;
using FateTakesYouHome.ViewModels;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// The application's Home Assistant service, and the tiles built on it, driven against the fake
/// server.
/// </summary>
/// <remarks>
/// The service marshals onto a dispatcher, so each test runs one on a thread of its own. Work it
/// posts then runs as it would on the app's UI thread, rather than queueing up unseen.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class HomeAssistantServiceTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // Validate() turns away anything shorter than a real long-lived token.
    private const string Token = "a-test-token-long-enough-to-pass-validation";

    private static JsonObject Light(string state, DateTimeOffset updated) => new()
    {
        ["entity_id"] = "light.kitchen_ceiling",
        ["state"] = state,
        ["attributes"] = new JsonObject { ["friendly_name"] = "Kitchen Ceiling" },
        ["last_changed"] = updated.ToString("O"),
        ["last_updated"] = updated.ToString("O"),
    };

    /// <summary>
    /// A change pushed while the snapshot is still being read survives it.
    /// </summary>
    /// <remarks>
    /// Named after the bug: the snapshot read the states, then six registries, then wrote the
    /// states it had read over the cache. Straight after a Home Assistant restart that is exactly
    /// when entities come up, so their tiles went back to Unavailable and stayed there until each
    /// one happened to change again.
    /// </remarks>
    [Fact]
    public async Task AStateChangeDuringTheSnapshotIsNotOverwrittenByIt()
    {
        DateTimeOffset restarted = DateTimeOffset.UtcNow;

        await using var server = new FakeHomeAssistantServer(Token)
        {
            States = [Light("unavailable", restarted)],
        };

        // The light comes up while the client is reading the registries.
        server.BeforeAnswering = async type =>
        {
            if (type == "config/area_registry/list")
            {
                await server.PushStateChangedAsync(
                    "light.kitchen_ceiling", Light("on", restarted.AddSeconds(5)));
            }
        };

        await using var harness = new Harness();
        await harness.ConnectAsync(server);

        Assert.Equal("on", harness.Service.Find("light.kitchen_ceiling")?.State);
    }

    /// <summary>
    /// Tapping an automation runs it now, as the README promises, rather than disabling it.
    /// </summary>
    /// <remarks>
    /// The tile used to treat an automation as something with an on and an off and send
    /// <c>homeassistant.toggle</c>, so a tap meant to run the evening routine switched it off.
    /// </remarks>
    [Fact]
    public async Task TappingAnAutomationTileTriggersIt()
    {
        await using var server = new FakeHomeAssistantServer(Token)
        {
            States =
            [
                new JsonObject
                {
                    ["entity_id"] = "automation.evening",
                    ["state"] = "on",
                    ["attributes"] = new JsonObject { ["friendly_name"] = "Evening" },
                },
            ],
        };

        await using var harness = new Harness();
        await harness.ConnectAsync(server);

        await harness.Dispatcher.InvokeAsync(async () =>
        {
            var tile = new EntityTileViewModel(
                harness.Service.Find("automation.evening")!, harness.Service);

            await tile.PrimaryCommand.ExecuteAsync(null);
        }).Task.Unwrap();

        JsonNode call = await server.WaitForCommandAsync("call_service", Patience);

        Assert.Equal("automation", call["domain"]!.GetValue<string>());
        Assert.Equal("trigger", call["service"]!.GetValue<string>());
        Assert.Equal("automation.evening", call["target"]!["entity_id"]!.GetValue<string>());
    }

    /// <summary>A service on a dispatcher thread of its own, with a log in a temporary folder.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("fate-tests-").FullName;
        private readonly AppLog _log;

        public Harness()
        {
            Dispatcher = StartDispatcher();
            _log = new AppLog(Path.Combine(_root, "logs"));
            Service = new HomeAssistantService(_log, Dispatcher);
        }

        public Dispatcher Dispatcher { get; }

        public HomeAssistantService Service { get; }

        /// <summary>Connects and waits for the first snapshot to land.</summary>
        public async Task ConnectAsync(FakeHomeAssistantServer server)
        {
            var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Service.SnapshotReloaded += (_, _) => reloaded.TrySetResult();

            await Service.ApplyConnectionAsync(new HaConnectionOptions
            {
                BaseUrl = server.BaseUrl,
                AccessToken = server.ExpectedToken,
                CommandTimeout = TimeSpan.FromSeconds(5),
            });

            await reloaded.Task.WaitAsync(Patience);
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            _log.Dispose();
            Dispatcher.InvokeShutdown();

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A straggling handle on a temp directory is not worth failing the run over.
            }
        }

        private static Dispatcher StartDispatcher()
        {
            var ready = new TaskCompletionSource<Dispatcher>();
            var thread = new Thread(() =>
            {
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            })
            {
                IsBackground = true,
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return ready.Task.Result;
        }
    }
}
