namespace RustPlusBot.Domain.Switches;

/// <summary>A paired Smart Switch the bot manages, surviving restarts. Guild- and server-scoped.</summary>
public sealed class SmartSwitch
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this switch belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The in-game smart-switch entity id.</summary>
    public ulong EntityId { get; set; }

    /// <summary>User-facing label; defaults to a generated "Switch &lt;EntityId&gt;" (the FCM event carries no name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The Discord message id of this switch's embed, or null until first posted.</summary>
    public ulong? MessageId { get; set; }

    /// <summary>The Discord user who accepted (validated) the pairing.</summary>
    public ulong PairedByUserId { get; set; }

    /// <summary>The last observed on/off state.</summary>
    public bool LastIsActive { get; set; }

    /// <summary>When the switch was accepted (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; set; }
}
