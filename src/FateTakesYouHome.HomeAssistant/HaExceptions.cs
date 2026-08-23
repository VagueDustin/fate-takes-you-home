namespace FateTakesYouHome.HomeAssistant;

/// <summary>Base class for every failure this client raises deliberately.</summary>
public class HaException : Exception
{
    public HaException(string message) : base(message) { }

    public HaException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The server rejected the access token. Retrying will not help — the user must supply a new one.
/// </summary>
public sealed class HaAuthenticationException : HaException
{
    public HaAuthenticationException(string message) : base(message) { }
}

/// <summary>A command was answered with <c>success: false</c>.</summary>
public sealed class HaCommandException : HaException
{
    public HaCommandException(string code, string message)
        : base($"Home Assistant refused the request ({code}): {message}")
    {
        Code = code;
        ServerMessage = message;
    }

    public string Code { get; }

    public string ServerMessage { get; }
}

/// <summary>The socket dropped, or was never established.</summary>
public sealed class HaConnectionException : HaException
{
    public HaConnectionException(string message) : base(message) { }

    public HaConnectionException(string message, Exception inner) : base(message, inner) { }
}
