namespace RustPlusBot.Abstractions.Events;

/// <summary>Raised when a Smart Switch's live on/off state is observed (prime or trigger).</summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local RustServer id the entity belongs to.</param>
/// <param name="EntityId">The in-game smart-switch entity id.</param>
/// <param name="IsActive">True when the switch is on.</param>
public sealed record SwitchStateChangedEvent(ulong GuildId, Guid ServerId, ulong EntityId, bool IsActive);
