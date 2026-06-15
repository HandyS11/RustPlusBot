namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Result of an initial socket connect attempt.</summary>
internal enum SocketConnectOutcome
{
    /// <summary>Socket connected.</summary>
    Connected = 0,

    /// <summary>The server rejected the player token (credential problem).</summary>
    AuthRejected = 1,

    /// <summary>The server could not be reached (network/down).</summary>
    Unreachable = 2,
}
