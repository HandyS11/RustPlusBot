namespace RustPlusBot.Abstractions.Events;

/// <summary>A storage monitor was paired in-game via FCM; the feature offers an "Add it?" prompt.</summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local Rust server id.</param>
/// <param name="EntityId">The in-game storage-monitor entity id.</param>
public sealed record StorageMonitorPairedEvent(ulong GuildId, Guid ServerId, ulong EntityId) : IPairedDeviceEvent;
