using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Vending.Ownership;
using RustPlusBot.Features.Vending.Searching;

namespace RustPlusBot.Features.Vending.Indexing;

/// <summary>One server's latest observed vending state.</summary>
/// <param name="WorldSize">The world size in game units, for grid maths.</param>
/// <param name="Machines">Every machine observed in the latest poll.</param>
internal sealed record ServerVendingState(uint WorldSize, IReadOnlyList<VendingMachineSnapshot> Machines);

/// <summary>
/// The live vending index, one entry per (guild, server). Rust re-sends the complete vending set on every
/// poll, so state is replaced wholesale — there is no merge, no eviction and no staleness bookkeeping.
/// Singleton, written by the hosted service and read by commands, hence the concurrent dictionary.
/// </summary>
internal sealed class VendingIndex : IVendingReadModel
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), ServerVendingState> _states = new();

    /// <inheritdoc />
    public bool HasData(ulong guildId, Guid serverId) => _states.ContainsKey((guildId, serverId));

    /// <inheritdoc />
    public IReadOnlyList<VendingOffer> Search(
        ulong guildId,
        Guid serverId,
        int itemId,
        MapGridStyle gridStyle)
    {
        if (!TryGet(guildId, serverId, out var state))
        {
            return [];
        }

        var matches = state.Machines
            .SelectMany(m => GridOwnership.ToOffers(m, state.WorldSize, gridStyle))
            .Where(o => o.Key.ItemId == itemId);
        return VendingSearch.Order(matches);
    }

    /// <summary>Replaces a server's state with the latest poll.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="machines">Every machine observed in this poll.</param>
    public void Replace(
        ulong guildId,
        Guid serverId,
        uint worldSize,
        IReadOnlyList<VendingMachineSnapshot> machines) =>
        _states[(guildId, serverId)] = new ServerVendingState(worldSize, machines);

    /// <summary>Drops a server's state, e.g. on disconnect.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    public void Clear(ulong guildId, Guid serverId) => _states.TryRemove((guildId, serverId), out _);

    /// <summary>Gets a server's latest state.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="state">The state, when present.</param>
    /// <returns>True when the server has observed state.</returns>
    public bool TryGet(ulong guildId, Guid serverId, [MaybeNullWhen(false)] out ServerVendingState state) =>
        _states.TryGetValue((guildId, serverId), out state);
}
