namespace RustPlusBot.Domain.Connections;

/// <summary>Live-connection lifecycle state for a server's socket.</summary>
public enum ConnectionStatus
{
    /// <summary>Attempting to connect (initial, after a swap, or after a drop).</summary>
    Connecting = 0,

    /// <summary>Socket up and the last heartbeat was healthy.</summary>
    Connected = 1,

    /// <summary>A valid active credential exists but the server is not answering; retrying with backoff.</summary>
    Unreachable = 2,

    /// <summary>No eligible credential (pool empty or every credential is Invalid).</summary>
    NoCredentials = 3,
}
