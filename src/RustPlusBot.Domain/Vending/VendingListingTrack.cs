using Persistord.Core.Abstractions;

namespace RustPlusBot.Domain.Vending;

/// <summary>A listing the team sells, registered by hand rather than read off a machine.</summary>
public sealed class VendingListingTrack : ICreatedAt
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this listing belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The Rust item id being sold.</summary>
    public int ItemId { get; set; }

    /// <summary>True when the item sold is a blueprint.</summary>
    public bool ItemIsBlueprint { get; set; }

    /// <summary>The Rust item id accepted as payment.</summary>
    public int CurrencyId { get; set; }

    /// <summary>True when the currency is a blueprint.</summary>
    public bool CurrencyIsBlueprint { get; set; }

    /// <summary>Items yielded by one order; at least 1.</summary>
    public int Quantity { get; set; }

    /// <summary>Currency charged for one order.</summary>
    public int CostPerOrder { get; set; }

    /// <summary>The Discord user who registered the listing, for display.</summary>
    public ulong RegisteredByUserId { get; set; }

    /// <summary>When the listing was registered (UTC). Stamped by Persistord's TimestampInterceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
