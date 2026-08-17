namespace RustPlusBot.Domain.Vending;

/// <summary>
/// A live undercut message in #vending. Stores the reference price it was rendered against so that
/// "did the owner reprice" is answerable without a second poll — an owner reprice deletes the message,
/// whereas a rival's move only edits it.
/// </summary>
public sealed class VendingNotification
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this notification belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The Rust item id being sold.</summary>
    public int ItemId { get; set; }

    /// <summary>True when the item sold is a blueprint.</summary>
    public bool ItemIsBlueprint { get; set; }

    /// <summary>The Rust item id accepted as payment.</summary>
    public int CurrencyId { get; set; }

    /// <summary>True when the currency is a blueprint.</summary>
    public bool CurrencyIsBlueprint { get; set; }

    /// <summary>The Discord message id of the posted notification.</summary>
    public ulong MessageId { get; set; }

    /// <summary>The owner's order quantity this message was rendered against.</summary>
    public int ReferenceQuantity { get; set; }

    /// <summary>The owner's order cost this message was rendered against.</summary>
    public int ReferenceCostPerOrder { get; set; }

    /// <summary>When the message was posted (UTC).</summary>
    public DateTimeOffset PostedUtc { get; set; }
}
