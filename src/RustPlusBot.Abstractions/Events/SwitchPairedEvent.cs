namespace RustPlusBot.Abstractions.Events;

/// <summary>Raised when a Smart Switch is paired in-game and resolved to a known server (awaiting user validation).</summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local RustServer id the entity belongs to.</param>
/// <param name="EntityId">The in-game smart-switch entity id.</param>
public sealed record SwitchPairedEvent(ulong GuildId, Guid ServerId, ulong EntityId) : IPairedDeviceEvent;
