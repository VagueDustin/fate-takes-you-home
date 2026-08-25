// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace FateTakesYouHome.HomeAssistant;

/// <summary>Everything needed to reach a Home Assistant instance.</summary>
public sealed class HaConnectionOptions
{
    /// <summary>Base URL, e.g. <c>http://homeassistant.local:8123</c> or <c>https://ha.example.com</c>.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>A Long-Lived Access Token from the user's Home Assistant profile page.</summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// Accept a TLS certificate that does not chain to a trusted root. Off by default; only
    /// meaningful for self-signed certificates on a LAN.
    /// </summary>
    public bool AllowInvalidCertificate { get; init; }

    /// <summary>How long to wait for the socket handshake and for each command's reply.</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Interval between keepalive pings once authenticated.</summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Reconnect backoff floor.</summary>
    public TimeSpan ReconnectMinDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Reconnect backoff ceiling.</summary>
    public TimeSpan ReconnectMaxDelay { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The WebSocket endpoint derived from <see cref="BaseUrl"/>.</summary>
    public Uri WebSocketUri => BuildUri("api/websocket", websocket: true);

    /// <summary>Builds an absolute REST URI for a path such as <c>api/config</c>.</summary>
    public Uri RestUri(string path) => BuildUri(path, websocket: false);

    private Uri BuildUri(string path, bool websocket)
    {
        string trimmed = BaseUrl.Trim().TrimEnd('/');
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("Home Assistant base URL is empty.");
        }

        // Tolerate a bare host: "homeassistant.local:8123" should mean http, not a relative URI.
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "http://" + trimmed;
        }

        var baseUri = new Uri(trimmed, UriKind.Absolute);

        if (websocket)
        {
            string scheme = baseUri.Scheme switch
            {
                "https" or "wss" => "wss",
                _ => "ws",
            };
            baseUri = new UriBuilder(baseUri) { Scheme = scheme }.Uri;
        }
        else if (baseUri.Scheme is "ws" or "wss")
        {
            string scheme = baseUri.Scheme == "wss" ? "https" : "http";
            baseUri = new UriBuilder(baseUri) { Scheme = scheme }.Uri;
        }

        // Preserve any path prefix (reverse-proxy subpath deployments are common).
        string basePath = baseUri.AbsolutePath.TrimEnd('/');
        return new Uri($"{baseUri.Scheme}://{baseUri.Authority}{basePath}/{path.TrimStart('/')}");
    }

    /// <summary>Validates the shape of the options without contacting the server.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            problems.Add("Server address is required.");
        }
        else
        {
            try
            {
                _ = WebSocketUri;
            }
            catch (Exception ex) when (ex is UriFormatException or InvalidOperationException)
            {
                problems.Add($"Server address is not a valid URL: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            problems.Add("Access token is required.");
        }
        else if (AccessToken.Trim().Length < 32)
        {
            problems.Add("That does not look like a Long-Lived Access Token — they are much longer.");
        }

        return problems;
    }
}
