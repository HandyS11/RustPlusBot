using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Events;

/// <summary>A managed storage monitor's contents were (re)read — on connect-prime or on an in-game change.</summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local Rust server id.</param>
/// <param name="EntityId">The in-game entity id (the discriminant — the feature filters to ids it manages).</param>
/// <param name="Contents">The contents snapshot carried on the read/broadcast.</param>
public sealed record StorageMonitorTriggeredEvent(
    ulong GuildId,
    Guid ServerId,
    ulong EntityId,
    StorageContentsSnapshot Contents);
