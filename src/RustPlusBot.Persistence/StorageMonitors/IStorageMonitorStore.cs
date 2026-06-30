using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.StorageMonitors;

namespace RustPlusBot.Persistence.StorageMonitors;

/// <summary>Persists managed Smart Storage Monitors (accepted pairings only; pending pairings stay in-memory).</summary>
public interface IStorageMonitorStore
{
    /// <summary>Adds a managed storage monitor and returns the persisted row.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game storage-monitor entity id.</param>
    /// <param name="name">The display name.</param>
    /// <param name="pairedByUserId">The user who accepted the pairing.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The persisted storage monitor.</returns>
    Task<SmartStorageMonitor> AddAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        ulong pairedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a storage monitor by identity, or null.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game storage-monitor entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The storage monitor, or null.</returns>
    Task<SmartStorageMonitor?> GetAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every managed storage monitor for a server.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The managed storage monitors for the server.</returns>
    Task<IReadOnlyList<SmartStorageMonitor>> ListByServerAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default);

    /// <summary>True when a managed storage monitor with this identity exists.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game storage-monitor entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if a matching storage monitor exists.</returns>
    Task<bool> ExistsAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a storage monitor (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game storage-monitor entity id.</param>
    /// <param name="name">The new display name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the rename has been persisted.</returns>
    Task RenameAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the embed message id (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game storage-monitor entity id.</param>
    /// <param name="messageId">The Discord embed message id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message id has been persisted.</returns>
    Task SetMessageIdAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        ulong messageId,
        CancellationToken cancellationToken = default);

    /// <summary>Sets a device's reachability (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game storage-monitor entity id.</param>
    /// <param name="reachability">The new reachability value.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the reachability has been persisted.</returns>
    Task SetReachabilityAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        DeviceReachability reachability,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a storage monitor (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game storage-monitor entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the storage monitor has been removed.</returns>
    Task RemoveAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default);
}
