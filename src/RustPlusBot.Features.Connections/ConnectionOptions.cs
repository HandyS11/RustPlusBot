namespace RustPlusBot.Features.Connections;

/// <summary>Connection feature configuration, bound from the "Connections" config section.</summary>
public sealed class ConnectionOptions
{
    /// <summary>How long to wait for a socket to connect before treating the server as unreachable.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>First delay before retrying after an unreachable result.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Cap on the exponential backoff between reconnect attempts.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often to send a heartbeat on a connected socket.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a single heartbeat may take before the socket is considered unreachable.</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How often to poll map markers for live-event detection. Default 10s.</summary>
    public TimeSpan MarkerPollInterval { get; set; } = TimeSpan.FromSeconds(10);
}
