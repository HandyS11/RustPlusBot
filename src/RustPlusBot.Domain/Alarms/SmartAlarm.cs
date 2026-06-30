using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Domain.Alarms;

/// <summary>A paired Smart Alarm the bot manages, surviving restarts. Guild- and server-scoped. Driven by the live socket (primed on connect, reacts to SmartDeviceTriggered) — the entity id is the switch-vs-alarm discriminant.</summary>
public sealed class SmartAlarm
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this alarm belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The in-game smart-alarm entity id.</summary>
    public ulong EntityId { get; set; }

    /// <summary>User-facing label; defaults to a generated "Alarm &lt;EntityId&gt;".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The Discord message id of this alarm's embed, or null until first posted.</summary>
    public ulong? MessageId { get; set; }

    /// <summary>The Discord user who accepted (validated) the pairing.</summary>
    public ulong PairedByUserId { get; set; }

    /// <summary>When the alarm was accepted (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>When true, a trigger going active pings @everyone in #alarms.</summary>
    public bool PingEveryone { get; set; }

    /// <summary>When true, a trigger going active relays the message into in-game team chat.</summary>
    public bool RelayToTeamChat { get; set; }

    /// <summary>The last observed on/off state from the in-game socket broadcast.</summary>
    public bool LastIsActive { get; set; }

    /// <summary>When the alarm most recently went active (UTC), or null if never triggered.</summary>
    public DateTimeOffset? LastTriggeredUtc { get; set; }

    /// <summary>Per-device reachability; defaults to Reachable. Orthogonal to whole-server connection status.</summary>
    public DeviceReachability Reachability { get; set; } = DeviceReachability.Reachable;
}
