using Persistord.Core.Abstractions;

namespace RustPlusBot.Domain.Vending;

/// <summary>A registered grid cell; every vending machine inside it counts as the team's own.</summary>
public sealed class VendingGridTrack : ICreatedAt
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this registration belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The grid reference, upper-cased, e.g. "D7".</summary>
    public string Grid { get; set; } = string.Empty;

    /// <summary>The Steam id of the player who registered the cell, for display.</summary>
    public ulong RegisteredBySteamId { get; set; }

    /// <summary>When the cell was registered (UTC). Stamped by Persistord's TimestampInterceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
