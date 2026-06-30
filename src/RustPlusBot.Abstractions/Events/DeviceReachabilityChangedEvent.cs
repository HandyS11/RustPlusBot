using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Events;

/// <summary>Raised when a managed device's reachability changes. Device-agnostic; relays filter by entity ownership.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The server id.</param>
/// <param name="EntityId">The in-game entity id.</param>
/// <param name="Reachability">The new reachability.</param>
public sealed record DeviceReachabilityChangedEvent(
    ulong GuildId,
    Guid ServerId,
    ulong EntityId,
    DeviceReachability Reachability);
