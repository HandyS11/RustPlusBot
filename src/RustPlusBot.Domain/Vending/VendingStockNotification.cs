using Persistord.Core.Abstractions;

namespace RustPlusBot.Domain.Vending;

/// <summary>
/// A live sell-out message in #vending, one per registered machine. Stores the sold-out set it was
/// rendered against so an owner restock deletes the message rather than silently editing it.
/// </summary>
public sealed class VendingStockNotification : IGuildScoped
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The server this notification belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The vending machine's marker id.</summary>
    public ulong MachineId { get; set; }

    /// <summary>The Discord message id of the posted notification.</summary>
    public ulong MessageId { get; set; }

    /// <summary>
    /// The sold-out set this message was rendered against: "*" when the whole machine was empty,
    /// otherwise the sold-out item ids sorted ascending and comma-joined. Compared whole, never parsed.
    /// </summary>
    public string SoldOutSignature { get; set; } = string.Empty;

    /// <summary>When the message was posted (UTC).</summary>
    public DateTimeOffset PostedUtc { get; set; }

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }
}
