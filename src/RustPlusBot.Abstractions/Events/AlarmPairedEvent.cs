namespace RustPlusBot.Abstractions.Events;

/// <summary>A Smart Alarm was paired in-game and needs validation before the bot manages it.</summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local Rust server id.</param>
/// <param name="EntityId">The in-game smart-alarm entity id.</param>
public sealed record AlarmPairedEvent(ulong GuildId, Guid ServerId, ulong EntityId) : IPairedDeviceEvent;
