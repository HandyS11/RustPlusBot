using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Devices;

/// <summary>
/// The persistence every managed smart-device type shares: check whether an identity is already
/// managed, persist an accepted pairing, read the rows back and mutate the bookkeeping the bot keeps
/// per device. One implementation per device feature (switches, storage monitors…); lets the shared
/// pairing scaffolding drive persistence without knowing which device type it is serving. Feature
/// store interfaces extend this with whatever is specific to their device.
/// </summary>
/// <typeparam name="TEntity">The persisted device row the store returns.</typeparam>
public interface IPairedDeviceStore<TEntity>
    where TEntity : class
{
    /// <summary>Adds a managed device and returns the persisted row.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game device entity id.</param>
    /// <param name="name">The display name.</param>
    /// <param name="pairedByUserId">The user who accepted the pairing.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The persisted device.</returns>
    Task<TEntity> AddAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        ulong pairedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a device by identity, or null.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game device entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The device, or null.</returns>
    Task<TEntity?> GetAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every managed device for a server, oldest first.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The managed devices for the server.</returns>
    Task<IReadOnlyList<TEntity>> ListByServerAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default);

    /// <summary>True when a managed device with this identity exists.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game device entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if a matching device exists.</returns>
    Task<bool> ExistsAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a device (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game device entity id.</param>
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
    /// <param name="entityId">The in-game device entity id.</param>
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
    /// <param name="entityId">The in-game device entity id.</param>
    /// <param name="reachability">The new reachability value.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the reachability has been persisted.</returns>
    Task SetReachabilityAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        DeviceReachability reachability,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a device (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game device entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the device has been removed.</returns>
    Task RemoveAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default);
}
