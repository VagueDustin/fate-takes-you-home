using System.Text.Json.Nodes;
using FateTakesYouHome.HomeAssistant;
using FateTakesYouHome.HomeAssistant.Models;
using Xunit;

namespace FateTakesYouHome.Tests;

/// <summary>
/// Drives the real client against a real WebSocket server speaking the Home Assistant protocol.
/// </summary>
/// <remarks>
/// Everything else in the suite tests pieces in isolation. The interesting failures in a protocol
/// client are in the sequencing — waiting for the greeting before sending credentials, matching a
/// reply to the right command id, coming back after the socket drops — and none of that is
/// reachable without something on the other end.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class HaClientIntegrationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static HaConnectionOptions OptionsFor(
        FakeHomeAssistantServer server, string? token = null) =>
        new()
        {
            BaseUrl = server.BaseUrl,
            AccessToken = token ?? server.ExpectedToken,

            // Tight timings: these tests wait on real sockets and should not take ten seconds each.
            CommandTimeout = TimeSpan.FromSeconds(5),
            PingInterval = TimeSpan.FromMilliseconds(300),
            ReconnectMinDelay = TimeSpan.FromMilliseconds(100),
            ReconnectMaxDelay = TimeSpan.FromSeconds(1),
        };

    private static JsonObject Light(string entityId, string state, int? brightness = null)
    {
        var attributes = new JsonObject
        {
            ["friendly_name"] = "Kitchen Ceiling",
            ["supported_color_modes"] = new JsonArray("brightness"),
        };

        if (brightness is not null)
        {
            attributes["brightness"] = brightness;
        }

        return new JsonObject
        {
            ["entity_id"] = entityId,
            ["state"] = state,
            ["attributes"] = attributes,
        };
    }

    // ------------------------------------------------------------------ handshake

    [Fact]
    public async Task ConnectsAndAuthenticates()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        client.Start();

        Assert.True(
            await FakeHomeAssistantServer.WaitUntilAsync(
                () => client.State == HaConnectionState.Connected, Patience),
            $"Never reached Connected; ended at {client.State}.");

        Assert.Equal(server.Version, client.ServerVersion);
    }

    /// <summary>
    /// Credentials must not be sent before the server has asked for them.
    /// </summary>
    [Fact]
    public async Task WaitsForTheGreetingBeforeSendingTheToken()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        client.Start();

        await server.WaitForCommandAsync("auth", Patience);

        // The auth frame must be the very first thing the client sent.
        Assert.Equal("auth", server.Received[0]["type"]!.GetValue<string>());
        Assert.Equal(server.ExpectedToken, server.Received[0]["access_token"]!.GetValue<string>());
    }

    /// <summary>
    /// A rejected token is terminal. Retrying a bad credential forever would look like a network
    /// problem and would hammer the server for no reason.
    /// </summary>
    [Fact]
    public async Task ARejectedTokenFailsWithoutRetrying()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server, "wrong-token"));

        string? detail = null;
        client.ConnectionStateChanged += (_, e) =>
        {
            if (e.State == HaConnectionState.Failed)
            {
                detail = e.Detail;
            }
        };

        client.Start();

        Assert.True(
            await FakeHomeAssistantServer.WaitUntilAsync(
                () => client.State == HaConnectionState.Failed, Patience),
            $"Expected Failed, ended at {client.State}.");

        Assert.Contains("token", detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // Give it long enough that a retry loop would have shown itself.
        int attempts = server.ConnectionCount;
        await Task.Delay(700);

        Assert.Equal(attempts, server.ConnectionCount);
    }

    [Fact]
    public async Task SubscribesToStateChangesOnConnect()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        client.Start();

        JsonNode subscribe = await server.WaitForCommandAsync("subscribe_events", Patience);

        Assert.Equal("state_changed", subscribe["event_type"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ commands

    [Fact]
    public async Task ReadsAndParsesTheStateMachine()
    {
        await using var server = new FakeHomeAssistantServer
        {
            States = [Light("light.kitchen_ceiling", "on", 191)],
        };

        await using var client = new HaClient(OptionsFor(server));
        client.Start();

        await FakeHomeAssistantServer.WaitUntilAsync(
            () => client.State == HaConnectionState.Connected, Patience);

        IReadOnlyList<HaEntityState> states = await client.GetStatesAsync();

        HaEntityState light = Assert.Single(states);
        Assert.Equal("light.kitchen_ceiling", light.EntityId);
        Assert.Equal("Kitchen Ceiling", light.FriendlyName);
        Assert.True(light.IsOn);
        Assert.Equal(191, light.AttrInt("brightness"));
    }

    [Fact]
    public async Task ReadsTheServerConfiguration()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        client.Start();
        await FakeHomeAssistantServer.WaitUntilAsync(
            () => client.State == HaConnectionState.Connected, Patience);

        HaConfig? config = await client.GetConfigAsync();

        Assert.Equal("Test House", config!.LocationName);
    }

    /// <summary>
    /// The wire shape of a service call is what actually changes somebody's house, so it is
    /// asserted field by field rather than just "it did not throw".
    /// </summary>
    [Fact]
    public async Task SendsAWellFormedServiceCall()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        client.Start();
        await FakeHomeAssistantServer.WaitUntilAsync(
            () => client.State == HaConnectionState.Connected, Patience);

        await client.SetLightBrightnessAsync("light.kitchen_ceiling", 40);

        JsonNode call = await server.WaitForCommandAsync("call_service", Patience);

        Assert.Equal("light", call["domain"]!.GetValue<string>());
        Assert.Equal("turn_on", call["service"]!.GetValue<string>());
        Assert.Equal("light.kitchen_ceiling", call["target"]!["entity_id"]!.GetValue<string>());
        Assert.Equal(40, call["service_data"]!["brightness_pct"]!.GetValue<int>());
    }

    /// <summary>Zero brightness means off, not a zero-brightness light that is still on.</summary>
    [Fact]
    public async Task ZeroBrightnessTurnsTheLightOff()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        client.Start();
        await FakeHomeAssistantServer.WaitUntilAsync(
            () => client.State == HaConnectionState.Connected, Patience);

        await client.SetLightBrightnessAsync("light.kitchen_ceiling", 0);

        JsonNode call = await server.WaitForCommandAsync("call_service", Patience);

        Assert.Equal("turn_off", call["service"]!.GetValue<string>());
    }

    /// <summary>
    /// Firing an automation by hand bypasses its conditions, which is Home Assistant's own default
    /// and what somebody pressing a button means.
    /// </summary>
    [Fact]
    public async Task TriggeringAnAutomationSkipsItsConditions()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        client.Start();
        await FakeHomeAssistantServer.WaitUntilAsync(
            () => client.State == HaConnectionState.Connected, Patience);

        await client.TriggerAutomationAsync("automation.evening");

        JsonNode call = await server.WaitForCommandAsync("call_service", Patience);

        Assert.Equal("automation", call["domain"]!.GetValue<string>());
        Assert.Equal("trigger", call["service"]!.GetValue<string>());
        Assert.True(call["service_data"]!["skip_condition"]!.GetValue<bool>());
    }

    /// <summary>
    /// A refused call must surface the server's own wording. "Something went wrong" helps nobody
    /// when Home Assistant already said exactly what was missing.
    /// </summary>
    [Fact]
    public async Task ARefusedServiceCallSurfacesTheServersOwnMessage()
    {
        await using var server = new FakeHomeAssistantServer
        {
            RefuseServiceCallsWith = "Service light.turn_on not found.",
        };

        await using var client = new HaClient(OptionsFor(server));

        client.Start();
        await FakeHomeAssistantServer.WaitUntilAsync(
            () => client.State == HaConnectionState.Connected, Patience);

        HaCommandException error = await Assert.ThrowsAsync<HaCommandException>(
            () => client.TurnOnAsync("light.missing"));

        Assert.Equal("service_not_found", error.Code);
        Assert.Equal("Service light.turn_on not found.", error.ServerMessage);
    }

    /// <summary>
    /// Floors arrived in core 2024.4. An older server answering "unknown command" is normal, and
    /// must not be treated as a failure.
    /// </summary>
    [Fact]
    public async Task AnUnknownCommandForFloorsDegradesToAnEmptyList()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        client.Start();
        await FakeHomeAssistantServer.WaitUntilAsync(
            () => client.State == HaConnectionState.Connected, Patience);

        // The fake answers config/floor_registry/list, so ask for something it does not know at
        // all by routing through the same tolerant path.
        IReadOnlyList<HaFloor> floors = await client.GetFloorsAsync();

        Assert.NotNull(floors);
    }

    // ------------------------------------------------------------------ events

    [Fact]
    public async Task RaisesStateChangedForAPushedEvent()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        HaStateChangedEventArgs? seen = null;
        client.StateChanged += (_, e) => seen = e;

        client.Start();
        await server.WaitForCommandAsync("subscribe_events", Patience);

        await server.PushStateChangedAsync(
            "light.kitchen_ceiling", Light("light.kitchen_ceiling", "on", 255));

        Assert.True(
            await FakeHomeAssistantServer.WaitUntilAsync(() => seen is not null, Patience),
            "No state_changed event reached the client.");

        Assert.Equal("light.kitchen_ceiling", seen!.EntityId);
        Assert.Equal(255, seen.NewState!.AttrInt("brightness"));
        Assert.False(seen.Removed);
    }

    [Fact]
    public async Task ANullNewStateMeansTheEntityWasRemoved()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        HaStateChangedEventArgs? seen = null;
        client.StateChanged += (_, e) => seen = e;

        client.Start();
        await server.WaitForCommandAsync("subscribe_events", Patience);

        await server.PushStateChangedAsync("light.gone", newState: null);

        Assert.True(
            await FakeHomeAssistantServer.WaitUntilAsync(() => seen is not null, Patience),
            "No state_changed event reached the client.");

        Assert.True(seen!.Removed);
    }

    // ------------------------------------------------------------------ resilience

    /// <summary>
    /// The whole point of the supervisor. A dropped socket must heal without anybody pressing
    /// anything.
    /// </summary>
    [Fact]
    public async Task ReconnectsAfterTheServerDropsTheConnection()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        client.Start();

        Assert.True(
            await FakeHomeAssistantServer.WaitUntilAsync(
                () => client.State == HaConnectionState.Connected, Patience),
            "Never connected in the first place.");

        Assert.Equal(1, server.ConnectionCount);

        server.DropConnection();

        Assert.True(
            await FakeHomeAssistantServer.WaitUntilAsync(
                () => server.ConnectionCount >= 2 && client.State == HaConnectionState.Connected,
                Patience),
            $"Did not reconnect; state {client.State}, {server.ConnectionCount} connections.");
    }

    /// <summary>
    /// Consumers re-read the whole state set on this, because changes during an outage were never
    /// delivered.
    /// </summary>
    [Fact]
    public async Task RaisesResynchronisedOnEveryConnection()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        int resyncs = 0;
        client.Resynchronised += (_, _) => Interlocked.Increment(ref resyncs);

        client.Start();

        Assert.True(
            await FakeHomeAssistantServer.WaitUntilAsync(() => Volatile.Read(ref resyncs) >= 1, Patience),
            "No initial resynchronisation.");

        server.DropConnection();

        Assert.True(
            await FakeHomeAssistantServer.WaitUntilAsync(() => Volatile.Read(ref resyncs) >= 2, Patience),
            "No resynchronisation after reconnecting.");
    }

    /// <summary>The keepalive must actually be sent, or a dead socket is never noticed.</summary>
    [Fact]
    public async Task SendsKeepalivePings()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        client.Start();
        await FakeHomeAssistantServer.WaitUntilAsync(
            () => client.State == HaConnectionState.Connected, Patience);

        Assert.True(
            await FakeHomeAssistantServer.WaitUntilAsync(
                () => server.Received.Any(n => n["type"]?.GetValue<string>() == "ping"), Patience),
            "The client never sent a keepalive ping.");

        // And it must stay up rather than treating its own ping as a problem.
        await Task.Delay(600);
        Assert.Equal(HaConnectionState.Connected, client.State);
    }

    [Fact]
    public async Task StoppingClosesTheConnectionAndStaysClosed()
    {
        await using var server = new FakeHomeAssistantServer();
        var client = new HaClient(OptionsFor(server));

        client.Start();
        await FakeHomeAssistantServer.WaitUntilAsync(
            () => client.State == HaConnectionState.Connected, Patience);

        await client.StopAsync();

        Assert.Equal(HaConnectionState.Disconnected, client.State);

        int attempts = server.ConnectionCount;
        await Task.Delay(500);

        Assert.Equal(attempts, server.ConnectionCount);

        await client.DisposeAsync();
    }

    [Fact]
    public async Task CommandsFailCleanlyWhenNotConnected()
    {
        await using var server = new FakeHomeAssistantServer();
        await using var client = new HaClient(OptionsFor(server));

        // Never started.
        await Assert.ThrowsAsync<HaConnectionException>(() => client.GetStatesAsync());
    }

    /// <summary>
    /// Replies are matched to commands by id, so overlapping requests must not cross over.
    /// </summary>
    [Fact]
    public async Task ConcurrentCommandsEachGetTheirOwnReply()
    {
        await using var server = new FakeHomeAssistantServer
        {
            States = [Light("light.a", "on"), Light("light.b", "off")],
            Areas = [new JsonObject { ["area_id"] = "kitchen", ["name"] = "Kitchen" }],
        };

        await using var client = new HaClient(OptionsFor(server));
        client.Start();

        await FakeHomeAssistantServer.WaitUntilAsync(
            () => client.State == HaConnectionState.Connected, Patience);

        Task<IReadOnlyList<HaEntityState>> states = client.GetStatesAsync();
        Task<IReadOnlyList<HaArea>> areas = client.GetAreasAsync();
        Task<HaConfig?> config = client.GetConfigAsync();

        await Task.WhenAll(states, areas, config);

        Assert.Equal(2, (await states).Count);
        Assert.Equal("Kitchen", Assert.Single(await areas).Name);
        Assert.Equal("Test House", (await config)!.LocationName);
    }

    /// <summary>
    /// Home Assistant refuses any frame whose id is not greater than the last it saw, so taking an
    /// id and writing the frame have to be a single atomic step.
    /// </summary>
    /// <remarks>
    /// Named after the bug: the id was allocated outside the send lock, so two callers could take
    /// 1 and 2, swap places waiting for the lock, and put 2 on the wire first. The server then
    /// refused the lower id with "id_reuse" — which is exactly what a real install did the first
    /// time Test connection raced the initial subscription.
    /// </remarks>
    [Fact]
    public async Task OverlappingCommandsPutTheirIdsOnTheWireInOrder()
    {
        await using var server = new FakeHomeAssistantServer
        {
            States = [Light("light.a", "on")],
        };

        await using var client = new HaClient(OptionsFor(server));
        client.Start();

        await FakeHomeAssistantServer.WaitUntilAsync(
            () => client.State == HaConnectionState.Connected, Patience);

        // Two overlapping sends would catch this only now and then. Enough of them and a write
        // that can happen out of order will.
        Task[] inFlight = Enumerable.Range(0, 40)
            .Select(_ => (Task)client.GetStatesAsync())
            .ToArray();

        // A regression surfaces here first: the server refuses the out-of-order frame and the
        // command it belonged to throws.
        await Task.WhenAll(inFlight);

        long[] ids = server.Received
            .Where(frame => frame["type"]?.GetValue<string>() != "auth")
            .Select(frame => frame["id"]?.GetValue<long>() ?? 0)
            .ToArray();

        Assert.NotEmpty(ids);

        for (int i = 1; i < ids.Length; i++)
        {
            Assert.True(
                ids[i] > ids[i - 1],
                $"Frame {i} arrived with id {ids[i]} after id {ids[i - 1]}.");
        }
    }
}
