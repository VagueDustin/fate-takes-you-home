// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.Services;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// The application's Home Assistant service, driven against the fake server.
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

        string root = Directory.CreateTempSubdirectory("fate-tests-").FullName;
        Dispatcher dispatcher = StartDispatcher();

        try
        {
            using var log = new AppLog(Path.Combine(root, "logs"));
            await using var service = new HomeAssistantService(log, dispatcher);

            var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            service.SnapshotReloaded += (_, _) => reloaded.TrySetResult();

            await service.ApplyConnectionAsync(new HaConnectionOptions
            {
                BaseUrl = server.BaseUrl,
                AccessToken = Token,
                CommandTimeout = TimeSpan.FromSeconds(5),
            });

            await reloaded.Task.WaitAsync(Patience);

            Assert.Equal("on", service.Find("light.kitchen_ceiling")?.State);
        }
        finally
        {
            dispatcher.InvokeShutdown();

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A straggling handle on a temp directory is not worth failing the run over.
            }
        }
    }
}
