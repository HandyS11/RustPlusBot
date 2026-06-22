namespace RustPlusBot.Abstractions.Events;

/// <summary>A managed smart device (switch/alarm/…) changed state in-game, as reported by the socket broadcast.</summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local Rust server id.</param>
/// <param name="EntityId">The in-game entity id (the discriminant — features filter to the ids they manage).</param>
/// <param name="IsActive">The new on/off state, carried directly on the broadcast (no re-read).</param>
public sealed record SmartDeviceTriggeredEvent(ulong GuildId, Guid ServerId, ulong EntityId, bool IsActive);
