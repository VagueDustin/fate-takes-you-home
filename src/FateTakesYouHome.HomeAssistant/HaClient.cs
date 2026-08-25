// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using FateTakesYouHome.HomeAssistant.Protocol;

namespace FateTakesYouHome.HomeAssistant;

/// <summary>
/// A supervised Home Assistant WebSocket connection.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="Start"/> once. The client then owns its own lifecycle: it connects,
/// authenticates, subscribes to <c>state_changed</c>, and reconnects with exponential backoff for
/// as long as it is running. Consumers observe <see cref="ConnectionStateChanged"/> and
/// <see cref="StateChanged"/> rather than driving the socket themselves.
/// </para>
/// <para>
/// A rejected access token is terminal: retrying cannot fix it, so the client moves to
/// <see cref="HaConnectionState.Failed"/> and stays there until it is reconfigured.
/// </para>
/// </remarks>
public sealed class HaClient : IAsyncDisposable
{
    private const int ReceiveChunkSize = 16 * 1024;

    private readonly HaConnectionOptions _options;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement?>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Action<string, Exception?>? _log;

    private CancellationTokenSource? _lifetime;
    private Task? _supervisor;
    private ClientWebSocket? _socket;
    private long _nextCommandId;
    private volatile HaConnectionState _state = HaConnectionState.Disconnected;
    private int _disposed;

    public HaClient(HaConnectionOptions options, Action<string, Exception?>? log = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log;
    }

    /// <summary>Fires on every lifecycle transition. Marshalling to a UI thread is the caller's job.</summary>
    public event EventHandler<HaConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>Fires for each entity state change pushed by the server.</summary>
    public event EventHandler<HaStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Fires after every successful (re)connection, once the subscription is live. Consumers should
    /// re-read the full state set here, because changes during the outage were never delivered.
    /// </summary>
    public event EventHandler? Resynchronised;

    public HaConnectionState State => _state;

    /// <summary>The Home Assistant core version reported at the last handshake.</summary>
    public string? ServerVersion { get; private set; }

    public HaConnectionOptions Options => _options;

    /// <summary>Begins the supervised connect loop. Safe to call twice; later calls are ignored.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (_supervisor is not null)
        {
            return;
        }

        _lifetime = new CancellationTokenSource();
        _supervisor = Task.Run(() => SuperviseAsync(_lifetime.Token));
    }

    /// <summary>Stops the loop and closes the socket.</summary>
    public async Task StopAsync()
    {
        if (_lifetime is null)
        {
            return;
        }

        try
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Already torn down.
        }

        if (_supervisor is not null)
        {
            try
            {
                await _supervisor.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        _supervisor = null;
        SetState(HaConnectionState.Disconnected);
    }

    // ------------------------------------------------------------------ supervision

    private async Task SuperviseAsync(CancellationToken ct)
    {
        TimeSpan delay = _options.ReconnectMinDelay;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(ct).ConfigureAwait(false);

                // A clean return means the socket closed without an error. Treat it as transient.
                delay = _options.ReconnectMinDelay;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (HaAuthenticationException ex)
            {
                _log?.Invoke("Authentication rejected; not retrying.", ex);
                SetState(HaConnectionState.Failed, ex.Message, ex);
                return;
            }
            catch (Exception ex)
            {
                _log?.Invoke("Connection attempt failed: " + ex.Message, ex);
            }
            finally
            {
                FailAllPending(new HaConnectionException("The connection closed before a reply arrived."));
                DisposeSocket();
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            SetState(
                HaConnectionState.Reconnecting,
                "Retrying in " + FormatDelay(delay) + ".",
                retryIn: delay);

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Exponential backoff, capped. Keeps a downed server from being hammered.
            delay = TimeSpan.FromMilliseconds(
                Math.Min(delay.TotalMilliseconds * 2, _options.ReconnectMaxDelay.TotalMilliseconds));
        }

        SetState(HaConnectionState.Disconnected);
    }

    private async Task RunConnectionAsync(CancellationToken ct)
    {
        SetState(HaConnectionState.Connecting, "Contacting " + _options.WebSocketUri.Host + "…");

        var socket = new ClientWebSocket();

        // We run an application-level ping instead, because Home Assistant answers it with a
        // routable frame we can time out on. A protocol-level ping tells us nothing.
        socket.Options.KeepAliveInterval = TimeSpan.Zero;

        if (_options.AllowInvalidCertificate)
        {
            socket.Options.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        }

        _socket = socket;
        _nextCommandId = 0;

        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct);
        handshake.CancelAfter(_options.CommandTimeout);

        try
        {
            await socket.ConnectAsync(_options.WebSocketUri, handshake.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HaConnectionException(
                "Timed out connecting to " + _options.WebSocketUri.Host
                + ". Is Home Assistant reachable from this PC?");
        }
        catch (WebSocketException ex)
        {
            throw new HaConnectionException(DescribeSocketFailure(ex), ex);
        }

        SetState(HaConnectionState.Authenticating, "Presenting access token…");

        // The pump must be running before the handshake, because the server speaks first.
        var inbound = new FrameQueue();
        Task pump = Task.Run(() => ReceivePumpAsync(socket, inbound, ct), CancellationToken.None);

        try
        {
            await AuthenticateAsync(socket, inbound, ct).ConfigureAwait(false);

            SetState(HaConnectionState.Connected, "Connected to Home Assistant " + ServerVersion + ".");

            Task router = RouteAsync(inbound, ct);
            Task pinger = PingLoopAsync(ct);

            await SubscribeToStateChangesAsync(ct).ConfigureAwait(false);
            Resynchronised?.Invoke(this, EventArgs.Empty);

            await Task.WhenAny(router, pump, pinger).ConfigureAwait(false);

            // Surface whichever task faulted, so the supervisor can log a real reason.
            foreach (Task finished in new[] { router, pump, pinger })
            {
                if (finished.IsFaulted)
                {
                    await finished.ConfigureAwait(false);
                }
            }
        }
        finally
        {
            inbound.Complete();
            await CloseSocketPolitelyAsync(socket).ConfigureAwait(false);
        }
    }

    private async Task AuthenticateAsync(ClientWebSocket socket, FrameQueue inbound, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.CommandTimeout);

        InboundEnvelope greeting;
        try
        {
            greeting = await inbound.ReadAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HaConnectionException("Home Assistant accepted the socket but never sent a greeting.");
        }

        if (greeting.Type != WireType.AuthRequired)
        {
            // Some proxies terminate the upgrade and then speak nonsense. Say so plainly.
            throw new HaConnectionException(
                "Expected an authentication challenge but received '" + greeting.Type + "'. "
                + "Check that the address points at Home Assistant itself and not a proxy error page.");
        }

        await SendRawAsync(socket, new Dictionary<string, object?>
        {
            ["type"] = WireType.Auth,
            ["access_token"] = _options.AccessToken.Trim(),
        }, ct).ConfigureAwait(false);

        InboundEnvelope verdict;
        try
        {
            verdict = await inbound.ReadAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HaConnectionException("Home Assistant never answered the authentication attempt.");
        }

        switch (verdict.Type)
        {
            case WireType.AuthOk:
                ServerVersion = verdict.HaVersion;
                return;

            case WireType.AuthInvalid:
                throw new HaAuthenticationException(
                    verdict.Message is { Length: > 0 } detail
                        ? "Home Assistant rejected the access token: " + detail
                        : "Home Assistant rejected the access token. Generate a new Long-Lived Access "
                          + "Token from your profile page and paste it again.");

            default:
                throw new HaConnectionException(
                    "Unexpected reply during authentication: '" + verdict.Type + "'.");
        }
    }

    private Task SubscribeToStateChangesAsync(CancellationToken ct) =>
        SendCommandAsync(new Dictionary<string, object?>
        {
            ["type"] = "subscribe_events",
            ["event_type"] = "state_changed",
        }, ct);

    // ------------------------------------------------------------------ receive and route

    /// <summary>
    /// Reads frames off the socket, reassembles fragmented messages, and hands whole envelopes to
    /// <paramref name="sink"/>. Runs for the life of the connection.
    /// </summary>
    private async Task ReceivePumpAsync(ClientWebSocket socket, FrameQueue sink, CancellationToken ct)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ReceiveChunkSize);
        var assembly = new ArrayBufferWriter<byte>(ReceiveChunkSize);

        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                }
                catch (WebSocketException ex)
                {
                    throw new HaConnectionException("The connection to Home Assistant dropped.", ex);
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new HaConnectionException(
                        "Home Assistant closed the connection (" + result.CloseStatus + "): "
                        + result.CloseStatusDescription);
                }

                assembly.Write(buffer.AsSpan(0, result.Count));

                if (!result.EndOfMessage)
                {
                    continue;
                }

                try
                {
                    InboundEnvelope? envelope = JsonSerializer.Deserialize<InboundEnvelope>(
                        assembly.WrittenSpan, WireJson.Options);

                    if (envelope is not null)
                    {
                        sink.Write(envelope);
                    }
                }
                catch (JsonException ex)
                {
                    // One malformed frame should not kill an otherwise working connection.
                    _log?.Invoke("Discarded an unparseable frame from Home Assistant.", ex);
                }
                finally
                {
                    assembly.Clear();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            sink.Complete();
        }
    }

    /// <summary>Dispatches envelopes to pending commands and to event subscribers.</summary>
    private async Task RouteAsync(FrameQueue inbound, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            InboundEnvelope envelope;
            try
            {
                envelope = await inbound.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (FrameQueue.ClosedException)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            switch (envelope.Type)
            {
                case WireType.Result:
                case WireType.Pong:
                    CompleteCommand(envelope);
                    break;

                case WireType.Event:
                    DispatchEvent(envelope);
                    break;

                default:
                    _log?.Invoke("Ignoring unsolicited '" + envelope.Type + "' frame.", null);
                    break;
            }
        }
    }

    private void CompleteCommand(InboundEnvelope envelope)
    {
        if (envelope.Id is not { } id
            || !_pending.TryRemove(id, out TaskCompletionSource<JsonElement?>? tcs))
        {
            return;
        }

        if (envelope.Success == false)
        {
            WireError error = envelope.Error ?? new WireError { Message = "No detail supplied." };
            tcs.TrySetException(new HaCommandException(error.CodeAsString, error.Message));
            return;
        }

        tcs.TrySetResult(envelope.Result);
    }

    private void DispatchEvent(InboundEnvelope envelope)
    {
        if (envelope.Event is not { } raw)
        {
            return;
        }

        EventEnvelope? evt;
        StateChangedData? data;
        try
        {
            evt = raw.Deserialize<EventEnvelope>(WireJson.Options);
            if (evt is null || evt.EventType != "state_changed")
            {
                return;
            }

            data = evt.Data.Deserialize<StateChangedData>(WireJson.Options);
        }
        catch (JsonException ex)
        {
            _log?.Invoke("Discarded an unparseable event payload.", ex);
            return;
        }

        if (data is null || string.IsNullOrEmpty(data.EntityId))
        {
            return;
        }

        StateChanged?.Invoke(this, new HaStateChangedEventArgs
        {
            EntityId = data.EntityId,
            OldState = data.OldState,
            NewState = data.NewState,
        });
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_options.PingInterval, ct).ConfigureAwait(false);

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(_options.CommandTimeout);

                await SendCommandAsync(
                    new Dictionary<string, object?> { ["type"] = WireType.Ping },
                    timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A missed pong means the socket is a zombie: the TCP connection can stay open long
                // after the server is gone. Fail out so the supervisor reconnects.
                throw new HaConnectionException("Home Assistant stopped answering keepalive pings.", ex);
            }
        }
    }

    // ------------------------------------------------------------------ sending

    /// <summary>Sends an identified command and waits for its <c>result</c> frame.</summary>
    internal async Task<JsonElement?> SendCommandAsync(
        Dictionary<string, object?> command, CancellationToken ct)
    {
        ClientWebSocket socket = _socket
            ?? throw new HaConnectionException("Not connected to Home Assistant.");

        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        long id;

        // Home Assistant requires every frame's id to be greater than the last one it saw on this
        // connection. Taking the number outside the lock is not enough: two senders can allocate
        // 1 and 2, then swap places waiting for the lock, and the server sees 2 before 1 and
        // refuses the lower one with "id_reuse". Allocating the id and writing the frame have to
        // be a single atomic step, so both happen under the send lock.
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            id = Interlocked.Increment(ref _nextCommandId);
            command["id"] = id;

            // Registered before the write, so a reply cannot arrive before there is anywhere to
            // route it.
            _pending[id] = tcs;

            try
            {
                await SendLockedAsync(socket, command, ct).ConfigureAwait(false);
            }
            catch
            {
                _pending.TryRemove(id, out _);
                throw;
            }
        }
        finally
        {
            _sendLock.Release();
        }

        await using CancellationTokenRegistration reg = ct.Register(static state =>
        {
            ((TaskCompletionSource<JsonElement?>)state!).TrySetCanceled();
        }, tcs);

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Writes an unidentified frame — the authentication handshake, which is the only exchange
    /// Home Assistant conducts without ids.
    /// </summary>
    private async Task SendRawAsync(
        ClientWebSocket socket, Dictionary<string, object?> payload, CancellationToken ct)
    {
        // ClientWebSocket permits exactly one send in flight.
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SendLockedAsync(socket, payload, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Serialises a frame and writes it.</summary>
    /// <remarks>
    /// The caller must hold <see cref="_sendLock"/>. Identified commands allocate their id inside
    /// that same lock, which is what keeps the ids on the wire in ascending order.
    /// </remarks>
    private async Task SendLockedAsync(
        ClientWebSocket socket, Dictionary<string, object?> payload, CancellationToken ct)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, WireJson.Options);

        try
        {
            if (socket.State != WebSocketState.Open)
            {
                throw new HaConnectionException("The connection to Home Assistant is not open.");
            }

            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct).ConfigureAwait(false);
        }
        catch (WebSocketException ex)
        {
            throw new HaConnectionException("Failed to send to Home Assistant.", ex);
        }
    }

    // ------------------------------------------------------------------ plumbing

    private void SetState(
        HaConnectionState state, string? detail = null, Exception? error = null, TimeSpan? retryIn = null)
    {
        if (_state == state && detail is null)
        {
            return;
        }

        _state = state;
        ConnectionStateChanged?.Invoke(this, new HaConnectionStateChangedEventArgs
        {
            State = state,
            Detail = detail,
            Error = error,
            RetryIn = retryIn,
        });
    }

    private void FailAllPending(Exception error)
    {
        foreach (long id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out TaskCompletionSource<JsonElement?>? tcs))
            {
                tcs.TrySetException(error);
            }
        }
    }

    private static async Task CloseSocketPolitelyAsync(ClientWebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await socket
                .CloseAsync(WebSocketCloseStatus.NormalClosure, "Client shutting down", cts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Closing is best-effort; the socket is being discarded either way.
        }
    }

    private void DisposeSocket()
    {
        try
        {
            _socket?.Dispose();
        }
        catch (Exception)
        {
            // Nothing useful to do here.
        }

        _socket = null;
    }

    private static string DescribeSocketFailure(WebSocketException ex) =>
        ex.WebSocketErrorCode switch
        {
            WebSocketError.NotAWebSocket =>
                "That address answered, but not with a WebSocket. Check the port and any reverse proxy.",
            WebSocketError.UnsupportedProtocol or WebSocketError.UnsupportedVersion =>
                "The server refused the WebSocket upgrade. A proxy in front of Home Assistant may be "
                + "stripping the upgrade headers.",
            _ => "Could not reach Home Assistant: " + ex.Message,
        };

    private static string FormatDelay(TimeSpan delay) =>
        delay.TotalSeconds < 60
            ? delay.TotalSeconds.ToString("0", System.Globalization.CultureInfo.CurrentCulture) + "s"
            : delay.TotalMinutes.ToString("0", System.Globalization.CultureInfo.CurrentCulture) + "m";

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);

        _lifetime?.Dispose();
        _sendLock.Dispose();
        DisposeSocket();
    }

    /// <summary>
    /// A single-consumer queue of parsed frames.
    /// </summary>
    /// <remarks>
    /// The handshake needs to read two specific messages before the general router starts, so the
    /// queue is separated from the routing logic rather than folded into it.
    /// </remarks>
    private sealed class FrameQueue
    {
        private readonly ConcurrentQueue<InboundEnvelope> _queue = new();
        private readonly SemaphoreSlim _signal = new(0);
        private volatile bool _completed;

        public void Write(InboundEnvelope envelope)
        {
            if (_completed)
            {
                return;
            }

            _queue.Enqueue(envelope);
            _signal.Release();
        }

        public void Complete()
        {
            if (_completed)
            {
                return;
            }

            _completed = true;

            try
            {
                _signal.Release();
            }
            catch (ObjectDisposedException)
            {
                // Racing with teardown; the reader is already gone.
            }
        }

        public async Task<InboundEnvelope> ReadAsync(CancellationToken ct)
        {
            while (true)
            {
                if (_queue.TryDequeue(out InboundEnvelope? envelope))
                {
                    return envelope;
                }

                if (_completed)
                {
                    throw new ClosedException();
                }

                await _signal.WaitAsync(ct).ConfigureAwait(false);
            }
        }

        internal sealed class ClosedException : Exception;
    }
}
