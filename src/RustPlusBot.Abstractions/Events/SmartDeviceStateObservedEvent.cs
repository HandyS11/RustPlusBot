namespace RustPlusBot.Abstractions.Events;

/// <summary>
/// A managed smart device's on/off state as <em>observed</em> by a read (connect prime, periodic sweep,
/// or manual refresh) — as opposed to <see cref="SmartDeviceTriggeredEvent"/>, which reports an in-game
/// broadcast. Consumers correct drifted state silently: an observation must never ping or relay.
/// </summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local Rust server id.</param>
/// <param name="EntityId">The in-game entity id (the discriminant — features filter to the ids they manage).</param>
/// <param name="IsActive">The observed on/off state.</param>
public sealed record SmartDeviceStateObservedEvent(ulong GuildId, Guid ServerId, ulong EntityId, bool IsActive);
