using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FateTakesYouHome.Tests;

/// <summary>
/// A Home Assistant WebSocket server, faithful enough to drive the real client against.
/// </summary>
/// <remarks>
/// <para>
/// Built on a raw <see cref="TcpListener"/> with a hand-written WebSocket handshake and framing.
/// <c>HttpListener</c> would be less code but needs a URL ACL registration on Windows for anything
/// but an elevated process, which would make the test suite fail on a normal developer machine —
/// and a test that only runs as administrator is a test that stops being run.
/// </para>
/// <para>
/// This exists because everything else in the suite tests the client's pieces in isolation. The
/// interesting failures in a protocol client are in the sequencing: does it wait for
/// <c>auth_required</c> before sending credentials, does it match replies to the right command id,
/// does it come back after the socket drops. None of that is reachable without something on the
/// other end.
/// </para>
/// </remarks>
internal sealed class FakeHomeAssistantServer : IAsyncDisposable
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentQueue<JsonNode> _received = new();
    private readonly List<Task> _connections = [];
    private readonly object _gate = new();

    private Stream? _current;
    private int _connectionCount;
    private bool _disposed;
    private long _lastCommandId;

    public FakeHomeAssistantServer(string expectedToken = "valid-token")
    {
        ExpectedToken = expectedToken;

        // Port 0 lets the OS pick a free one, so parallel test runs cannot collide.
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _ = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>The token that will be accepted. Anything else gets <c>auth_invalid</c>.</summary>
    public string ExpectedToken { get; }

    /// <summary>Home Assistant version reported at the handshake.</summary>
    public string Version { get; set; } = "2026.8.0";

    /// <summary>What <c>get_states</c> returns. Set before connecting.</summary>
    public JsonArray States { get; set; } = [];

    /// <summary>What <c>config/area_registry/list</c> returns.</summary>
    public JsonArray Areas { get; set; } = [];

    /// <summary>What <c>config/entity_registry/list</c> returns.</summary>
    public JsonArray EntityRegistry { get; set; } = [];

    /// <summary>
    /// When set, every <c>call_service</c> is refused with this message.
    /// </summary>
    /// <remarks>
    /// Home Assistant refuses service calls for ordinary reasons — an entity that has gone away, a
    /// service that does not exist on that platform — and the client has to surface the server's
    /// own wording rather than a generic failure.
    /// </remarks>
    public string? RefuseServiceCallsWith { get; set; }

    /// <summary>How many times a client has completed the WebSocket handshake.</summary>
    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    /// <summary>Every command frame the server has received, in order.</summary>
    public IReadOnlyList<JsonNode> Received => _received.ToArray();

    /// <summary>Waits for a command of the given type to arrive.</summary>
    public async Task<JsonNode> WaitForCommandAsync(string type, TimeSpan? timeout = null)
    {
        JsonNode? found = await WaitForAsync(
            () => _received.FirstOrDefault(n => n["type"]?.GetValue<string>() == type),
            timeout).ConfigureAwait(false);

        return found ?? throw new TimeoutException($"No '{type}' command arrived.");
    }

    /// <summary>Waits until a condition holds, polling. Returns null on timeout.</summary>
    public static async Task<T?> WaitForAsync<T>(Func<T?> probe, TimeSpan? timeout = null)
        where T : class
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (DateTime.UtcNow < deadline)
        {
            T? value = probe();

            if (value is not null)
            {
                return value;
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Waits until a condition is true. Returns false on timeout.</summary>
    public static async Task<bool> WaitUntilAsync(Func<bool> probe, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (DateTime.UtcNow < deadline)
        {
            if (probe())
            {
                return true;
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>Pushes a <c>state_changed</c> event to the connected client.</summary>
    public async Task PushStateChangedAsync(string entityId, JsonNode? newState, long subscriptionId = 1)
    {
        var frame = new JsonObject
        {
            ["id"] = subscriptionId,
            ["type"] = "event",
            ["event"] = new JsonObject
            {
                ["event_type"] = "state_changed",
                ["time_fired"] = DateTimeOffset.UtcNow.ToString("O"),
                ["data"] = new JsonObject
                {
                    ["entity_id"] = entityId,
                    ["old_state"] = null,
                    ["new_state"] = newState,
                },
            },
        };

        await SendAsync(frame).ConfigureAwait(false);
    }

    /// <summary>Drops the current connection without a close handshake, as a crash would.</summary>
    public void DropConnection()
    {
        lock (_gate)
        {
            try
            {
                _current?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already gone.
            }

            _current = null;
        }
    }

    // ------------------------------------------------------------------ plumbing

    private async Task AcceptLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
                                          or SocketException)
            {
                return;
            }

            Task connection = Task.Run(() => ServeAsync(client));

            lock (_gate)
            {
                _connections.Add(connection);
            }
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using TcpClient owned = client;
        NetworkStream stream = owned.GetStream();

        try
        {
            if (!await PerformHandshakeAsync(stream).ConfigureAwait(false))
            {
                return;
            }

            lock (_gate)
            {
                _current = stream;
            }

            Interlocked.Increment(ref _connectionCount);

            // Ids are scoped to a connection: a reconnect legitimately starts again from 1.
            Interlocked.Exchange(ref _lastCommandId, 0);

            // Home Assistant speaks first.
            await SendAsync(new JsonObject
            {
                ["type"] = "auth_required",
                ["ha_version"] = Version,
            }).ConfigureAwait(false);

            await ReadLoopAsync(stream).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException
                                      or InvalidOperationException or SocketException)
        {
            // A dropped connection is a scenario under test, not a failure.
        }
    }

    /// <summary>The RFC 6455 opening handshake.</summary>
    private static async Task<bool> PerformHandshakeAsync(Stream stream)
    {
        var request = new StringBuilder();
        var buffer = new byte[1];

        // Read headers up to the blank line. Small and slow, but a handshake is tiny.
        while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            int read = await stream.ReadAsync(buffer).ConfigureAwait(false);

            if (read == 0)
            {
                return false;
            }

            request.Append((char)buffer[0]);

            if (request.Length > 16 * 1024)
            {
                return false;
            }
        }

        string? key = request.ToString()
            .Split("\r\n")
            .FirstOrDefault(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            ?[18..]
            .Trim();

        if (key is null)
        {
            return false;
        }

        string accept = Convert.ToBase64String(
            SHA1.HashData(Encoding.UTF8.GetBytes(key + WebSocketGuid)));

        byte[] response = Encoding.UTF8.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\n"
            + "Upgrade: websocket\r\n"
            + "Connection: Upgrade\r\n"
            + $"Sec-WebSocket-Accept: {accept}\r\n\r\n");

        await stream.WriteAsync(response).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);

        return true;
    }

    private async Task ReadLoopAsync(Stream stream)
    {
        while (!_lifetime.IsCancellationRequested)
        {
            string? message = await ReadFrameAsync(stream).ConfigureAwait(false);

            if (message is null)
            {
                return;
            }

            JsonNode? node;

            try
            {
                node = JsonNode.Parse(message);
            }
            catch (JsonException)
            {
                continue;
            }

            if (node is null)
            {
                continue;
            }

            _received.Enqueue(node);
            await HandleAsync(node).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(JsonNode command)
    {
        string type = command["type"]?.GetValue<string>() ?? string.Empty;
        long id = command["id"]?.GetValue<long>() ?? 0;

        // Home Assistant refuses any identified frame whose id is not greater than the last it saw
        // on this connection, and the real server is the only thing that used to say so — a client
        // that let two sends race could allocate ids in order and still put them on the wire out
        // of order, passing every test here and failing against a real house. Authentication is
        // exempt: it is the one exchange conducted without ids.
        if (type != "auth")
        {
            if (id <= Interlocked.Read(ref _lastCommandId))
            {
                await SendAsync(new JsonObject
                {
                    ["id"] = id,
                    ["type"] = "result",
                    ["success"] = false,
                    ["error"] = new JsonObject
                    {
                        ["code"] = "id_reuse",
                        ["message"] = "Identifier values have to increase.",
                    },
                }).ConfigureAwait(false);
                return;
            }

            Interlocked.Exchange(ref _lastCommandId, id);
        }

        switch (type)
        {
            case "auth":
                bool ok = command["access_token"]?.GetValue<string>() == ExpectedToken;

                await SendAsync(ok
                    ? new JsonObject { ["type"] = "auth_ok", ["ha_version"] = Version }
                    : new JsonObject { ["type"] = "auth_invalid", ["message"] = "Invalid access token" })
                    .ConfigureAwait(false);
                break;

            case "ping":
                await SendAsync(new JsonObject { ["id"] = id, ["type"] = "pong" }).ConfigureAwait(false);
                break;

            case "get_states":
                await ResultAsync(id, States.DeepClone()).ConfigureAwait(false);
                break;

            case "config/area_registry/list":
                await ResultAsync(id, Areas.DeepClone()).ConfigureAwait(false);
                break;

            case "config/entity_registry/list":
                await ResultAsync(id, EntityRegistry.DeepClone()).ConfigureAwait(false);
                break;

            case "config/device_registry/list":
            case "config/floor_registry/list":
                await ResultAsync(id, new JsonArray()).ConfigureAwait(false);
                break;

            case "get_services":
                await ResultAsync(id, new JsonObject()).ConfigureAwait(false);
                break;

            case "get_config":
                await ResultAsync(id, new JsonObject
                {
                    ["location_name"] = "Test House",
                    ["version"] = Version,
                }).ConfigureAwait(false);
                break;

            case "auth/current_user":
                await ResultAsync(id, new JsonObject
                {
                    ["id"] = "user-1",
                    ["name"] = "Test User",
                    ["is_admin"] = true,
                }).ConfigureAwait(false);
                break;

            case "call_service" when RefuseServiceCallsWith is { } reason:
                await SendAsync(new JsonObject
                {
                    ["id"] = id,
                    ["type"] = "result",
                    ["success"] = false,
                    ["error"] = new JsonObject
                    {
                        ["code"] = "service_not_found",
                        ["message"] = reason,
                    },
                }).ConfigureAwait(false);
                break;

            case "subscribe_events":
            case "call_service":
                await ResultAsync(id, null).ConfigureAwait(false);
                break;

            default:
                await SendAsync(new JsonObject
                {
                    ["id"] = id,
                    ["type"] = "result",
                    ["success"] = false,
                    ["error"] = new JsonObject
                    {
                        ["code"] = "unknown_command",
                        ["message"] = $"Unknown command {type}",
                    },
                }).ConfigureAwait(false);
                break;
        }
    }

    private Task ResultAsync(long id, JsonNode? result) =>
        SendAsync(new JsonObject
        {
            ["id"] = id,
            ["type"] = "result",
            ["success"] = true,
            ["result"] = result,
        });

    private async Task SendAsync(JsonNode frame)
    {
        Stream? stream;

        lock (_gate)
        {
            stream = _current;
        }

        if (stream is null)
        {
            return;
        }

        byte[] payload = Encoding.UTF8.GetBytes(frame.ToJsonString());

        try
        {
            await stream.WriteAsync(EncodeFrame(payload)).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The client went away mid-write.
        }
    }

    /// <summary>Encodes a single unmasked text frame. Servers never mask.</summary>
    private static byte[] EncodeFrame(byte[] payload)
    {
        using var buffer = new MemoryStream();

        buffer.WriteByte(0x81); // FIN + text opcode

        if (payload.Length < 126)
        {
            buffer.WriteByte((byte)payload.Length);
        }
        else if (payload.Length <= ushort.MaxValue)
        {
            buffer.WriteByte(126);
            buffer.WriteByte((byte)(payload.Length >> 8));
            buffer.WriteByte((byte)(payload.Length & 0xFF));
        }
        else
        {
            buffer.WriteByte(127);

            for (int shift = 56; shift >= 0; shift -= 8)
            {
                buffer.WriteByte((byte)((long)payload.Length >> shift));
            }
        }

        buffer.Write(payload);
        return buffer.ToArray();
    }

    /// <summary>Reads one text frame, reassembling continuations. Clients always mask.</summary>
    private static async Task<string?> ReadFrameAsync(Stream stream)
    {
        var assembled = new MemoryStream();

        while (true)
        {
            byte[] header = new byte[2];

            if (!await ReadExactAsync(stream, header).ConfigureAwait(false))
            {
                return null;
            }

            bool fin = (header[0] & 0x80) != 0;
            int opcode = header[0] & 0x0F;
            bool masked = (header[1] & 0x80) != 0;
            long length = header[1] & 0x7F;

            if (opcode == 0x8)
            {
                return null; // Close.
            }

            if (length == 126)
            {
                byte[] extended = new byte[2];

                if (!await ReadExactAsync(stream, extended).ConfigureAwait(false))
                {
                    return null;
                }

                length = (extended[0] << 8) | extended[1];
            }
            else if (length == 127)
            {
                byte[] extended = new byte[8];

                if (!await ReadExactAsync(stream, extended).ConfigureAwait(false))
                {
                    return null;
                }

                length = 0;

                foreach (byte b in extended)
                {
                    length = (length << 8) | b;
                }
            }

            byte[] mask = new byte[4];

            if (masked && !await ReadExactAsync(stream, mask).ConfigureAwait(false))
            {
                return null;
            }

            byte[] payload = new byte[length];

            if (length > 0 && !await ReadExactAsync(stream, payload).ConfigureAwait(false))
            {
                return null;
            }

            if (masked)
            {
                for (int i = 0; i < payload.Length; i++)
                {
                    payload[i] ^= mask[i % 4];
                }
            }

            assembled.Write(payload);

            if (fin)
            {
                return Encoding.UTF8.GetString(assembled.ToArray());
            }
        }
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset)).ConfigureAwait(false);

            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _lifetime.CancelAsync().ConfigureAwait(false);

        DropConnection();
        _listener.Stop();

        Task[] pending;

        lock (_gate)
        {
            pending = [.. _connections];
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException)
        {
            // Shutting down; a stuck connection task is not worth failing a test over.
        }

        _lifetime.Dispose();
    }
}
