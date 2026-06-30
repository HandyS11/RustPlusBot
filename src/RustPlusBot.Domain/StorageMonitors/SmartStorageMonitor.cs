using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Domain.StorageMonitors;

/// <summary>A paired Smart Storage Monitor the bot manages, surviving restarts. Guild- and server-scoped.</summary>
public sealed class SmartStorageMonitor
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this monitor belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The in-game storage-monitor entity id.</summary>
    public ulong EntityId { get; set; }

    /// <summary>User-facing label; defaults to a generated "Storage Monitor &lt;EntityId&gt;" (the FCM event carries no name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The Discord message id of this monitor's embed, or null until first posted.</summary>
    public ulong? MessageId { get; set; }

    /// <summary>The Discord user who accepted (validated) the pairing.</summary>
    public ulong PairedByUserId { get; set; }

    /// <summary>When the monitor was accepted (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Per-device reachability; defaults to Reachable. Orthogonal to whole-server connection status.</summary>
    public DeviceReachability Reachability { get; set; } = DeviceReachability.Reachable;
}
