namespace RustPlusBot.Domain.Entities;

/// <summary>A paired in-game smart device, discovered via FCM pairing (populated in subsystem 1).</summary>
public sealed class PairedEntity
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this entity belongs to.</summary>
    public Guid RustServerId { get; set; }

    /// <summary>The in-game entity id.</summary>
    public ulong EntityId { get; set; }

    /// <summary>The device kind.</summary>
    public PairedEntityKind Kind { get; set; }

    /// <summary>User-facing label.</summary>
    public string Name { get; set; } = string.Empty;
}
