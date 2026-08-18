using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Vending;

/// <summary>Reads the live vending index built from the marker poll. Singleton; synchronous.</summary>
public interface IVendingReadModel
{
    /// <summary>True when a poll has been observed for this server since the last connect.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <returns>True when the index holds data.</returns>
    bool HasData(ulong guildId, Guid serverId);

    /// <summary>
    /// Finds every offer of an item, in-stock first and then cheapest per item.
    /// </summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="itemId">The Rust item id to search for.</param>
    /// <param name="gridStyle">Which grid convention to label positions with.</param>
    /// <returns>The matching offers in display order; empty when nothing matches or no poll has landed.</returns>
    IReadOnlyList<VendingOffer> Search(ulong guildId, Guid serverId, int itemId, MapGridStyle gridStyle);
}
