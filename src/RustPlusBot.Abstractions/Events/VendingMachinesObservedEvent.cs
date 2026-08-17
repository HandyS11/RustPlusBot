using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Events;

/// <summary>
/// Published on every marker poll with the server's complete vending-machine set. Rust re-sends the
/// full set each poll, so consumers replace their state wholesale rather than diffing.
/// </summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The target server id.</param>
/// <param name="WorldSize">The world size in game units, for grid-label maths; 0 when dimensions are unavailable.</param>
/// <param name="Machines">Every player vending machine observed in this poll.</param>
public sealed record VendingMachinesObservedEvent(
    ulong GuildId,
    Guid ServerId,
    uint WorldSize,
    IReadOnlyList<VendingMachineSnapshot> Machines);
