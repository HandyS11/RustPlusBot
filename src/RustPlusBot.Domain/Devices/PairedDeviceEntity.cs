using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Domain.Devices;

/// <summary>
/// The identity and bookkeeping every paired smart device the bot manages carries: who owns it, which
/// server and in-game entity it is, where its embed lives and whether it still answers. Not an entity
/// type of its own — EF maps each derived device to its own table, this base only shares the columns.
/// </summary>
public abstract class PairedDeviceEntity
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this device belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The in-game entity id.</summary>
    public ulong EntityId { get; set; }

    /// <summary>User-facing label; defaults to a generated name (the FCM pairing event carries none).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The Discord message id of this device's embed, or null until first posted.</summary>
    public ulong? MessageId { get; set; }

    /// <summary>The Discord user who accepted (validated) the pairing.</summary>
    public ulong PairedByUserId { get; set; }

    /// <summary>When the pairing was accepted (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Per-device reachability; defaults to Reachable. Orthogonal to whole-server connection status.</summary>
    public DeviceReachability Reachability { get; set; } = DeviceReachability.Reachable;
}
