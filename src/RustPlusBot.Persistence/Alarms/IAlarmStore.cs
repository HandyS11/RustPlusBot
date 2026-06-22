using RustPlusBot.Domain.Alarms;

namespace RustPlusBot.Persistence.Alarms;

/// <summary>Persists managed Smart Alarms (accepted pairings only; pending pairings stay in-memory).</summary>
public interface IAlarmStore
{
    /// <summary>Adds a managed alarm and returns the persisted row.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-alarm entity id.</param>
    /// <param name="name">The display name.</param>
    /// <param name="pairedByUserId">The user who accepted the pairing.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The persisted alarm.</returns>
    Task<SmartAlarm> AddAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        ulong pairedByUserId,
        CancellationToken ct = default);

    /// <summary>Gets an alarm by identity, or null.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-alarm entity id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The alarm, or null.</returns>
    Task<SmartAlarm?> GetAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken ct = default);

    /// <summary>Lists every managed alarm for a server, oldest first.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The managed alarms for the server, ordered by creation time ascending.</returns>
    Task<IReadOnlyList<SmartAlarm>> ListByServerAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken ct = default);

    /// <summary>True when a managed alarm with this identity exists.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-alarm entity id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True if a matching alarm exists.</returns>
    Task<bool> ExistsAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken ct = default);

    /// <summary>Renames an alarm (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-alarm entity id.</param>
    /// <param name="name">The new display name.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the rename has been persisted.</returns>
    Task RenameAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        CancellationToken ct = default);

    /// <summary>Sets the embed message id (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-alarm entity id.</param>
    /// <param name="messageId">The Discord embed message id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the message id has been persisted.</returns>
    Task SetMessageIdAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        ulong messageId,
        CancellationToken ct = default);

    /// <summary>Sets whether a trigger going active pings @everyone in the #alarms channel (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-alarm entity id.</param>
    /// <param name="value">True to ping @everyone; false to suppress.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the flag has been persisted.</returns>
    Task SetPingEveryoneAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool value,
        CancellationToken ct = default);

    /// <summary>Sets whether a trigger going active relays the message into in-game team chat (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-alarm entity id.</param>
    /// <param name="value">True to relay; false to suppress.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the flag has been persisted.</returns>
    Task SetRelayToTeamChatAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool value,
        CancellationToken ct = default);

    /// <summary>Updates the alarm's on/off state; stamps the last-triggered time only when going active (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-alarm entity id.</param>
    /// <param name="isActive">The new on/off state.</param>
    /// <param name="triggeredUtc">When it went active (UTC); pass non-null only on the active edge — when null, the existing last-triggered time is kept.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the state has been persisted.</returns>
    Task UpdateStateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool isActive,
        DateTimeOffset? triggeredUtc,
        CancellationToken ct = default);

    /// <summary>Removes an alarm (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-alarm entity id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the alarm has been removed.</returns>
    Task RemoveAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken ct = default);
}
