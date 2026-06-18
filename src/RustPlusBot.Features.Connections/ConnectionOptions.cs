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

    /// <summary>How often to poll map markers for live-event detection. Default 5s.</summary>
    public TimeSpan MarkerPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Faster poll cadence used while a CH47 marker is live (to catch its brief oil-rig visit). Default 2s.</summary>
    public TimeSpan MarkerPollFastInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Distance (world units) within which a CH47 is considered to be "at" an oil rig. Default 150.</summary>
    public float RigRadius { get; set; } = 150f;

    /// <summary>How long an oil rig stays in the combat (Active) phase before the crate becomes lootable. Default 15m.</summary>
    public TimeSpan RigActiveWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long an oil rig stays dormant (Offline) before the crate respawns (Online). Default 15m.</summary>
    public TimeSpan RigOfflineWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How often the rig-timer tick advances rig phases and emits timed boundary events. Default 30s.</summary>
    public TimeSpan RigTickInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a member must be still (and online + alive) before being flagged AFK. Default 5m.</summary>
    public TimeSpan AfkThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Movement tolerance (world units) below which a member is considered still. Default 1.</summary>
    public float AfkEpsilon { get; set; } = 1f;
}
