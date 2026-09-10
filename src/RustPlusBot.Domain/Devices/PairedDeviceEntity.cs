using Persistord.Core.Abstractions;
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Domain.Devices;

/// <summary>
/// The identity and bookkeeping every paired smart device the bot manages carries: who owns it, which
/// server and in-game entity it is, where its embed lives and whether it still answers.
/// </summary>
/// <remarks>
/// This is a plain code-sharing base, deliberately <em>not</em> an EF Core entity type: <c>BotDbContext</c>
/// calls <c>modelBuilder.Ignore&lt;PairedDeviceEntity&gt;()</c> so EF never maps it and never treats
/// <see cref="Switches.SmartSwitch"/> and <see cref="StorageMonitors.SmartStorageMonitor"/> as an
/// inheritance hierarchy. Without that <c>Ignore</c>, adding a <c>DbSet</c> or a navigation targeting this
/// type would pull the base into the model and silently collapse both device tables into one
/// table-per-hierarchy table. Each derived device keeps its own table; this base only shares the columns.
/// </remarks>
public abstract class PairedDeviceEntity : IGuildScoped, ICreatedAt
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

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

    /// <summary>Per-device reachability; defaults to Reachable. Orthogonal to whole-server connection status.</summary>
    public DeviceReachability Reachability { get; set; } = DeviceReachability.Reachable;

    /// <summary>When the pairing was accepted (UTC). Stamped by Persistord's TimestampInterceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }
}
