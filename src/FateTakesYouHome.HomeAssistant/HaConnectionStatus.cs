namespace FateTakesYouHome.HomeAssistant;

/// <summary>Where the client currently is in its connection lifecycle.</summary>
public enum HaConnectionState
{
    /// <summary>Not started, or deliberately stopped.</summary>
    Disconnected,

    /// <summary>Opening the socket.</summary>
    Connecting,

    /// <summary>Socket is open; exchanging the auth handshake.</summary>
    Authenticating,

    /// <summary>Authenticated and receiving events.</summary>
    Connected,

    /// <summary>Lost the socket; waiting out the backoff before trying again.</summary>
    Reconnecting,

    /// <summary>Stopped for a reason retrying cannot fix, such as a rejected token.</summary>
    Failed,
}

/// <summary>Raised whenever <see cref="HaConnectionState"/> changes.</summary>
public sealed class HaConnectionStateChangedEventArgs : EventArgs
{
    public required HaConnectionState State { get; init; }

    /// <summary>Human-readable detail. Safe to show in the UI; never contains the token.</summary>
    public string? Detail { get; init; }

    /// <summary>Populated when <see cref="State"/> is <see cref="HaConnectionState.Failed"/>.</summary>
    public Exception? Error { get; init; }

    /// <summary>How long until the next attempt, when reconnecting.</summary>
    public TimeSpan? RetryIn { get; init; }
}

/// <summary>Raised for every <c>state_changed</c> event the server pushes.</summary>
public sealed class HaStateChangedEventArgs : EventArgs
{
    public required string EntityId { get; init; }

    public Models.HaEntityState? OldState { get; init; }

    public Models.HaEntityState? NewState { get; init; }

    /// <summary>True when the entity was removed from the state machine.</summary>
    public bool Removed => NewState is null;
}
