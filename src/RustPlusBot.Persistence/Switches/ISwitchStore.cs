using RustPlusBot.Domain.Switches;

namespace RustPlusBot.Persistence.Switches;

/// <summary>Persists managed Smart Switches (accepted pairings only; pending pairings stay in-memory).</summary>
public interface ISwitchStore
{
    /// <summary>Adds a managed switch and returns the persisted row.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-switch entity id.</param>
    /// <param name="name">The display name.</param>
    /// <param name="pairedByUserId">The user who accepted the pairing.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The persisted switch.</returns>
    Task<SmartSwitch> AddAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        string name,
        ulong pairedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a switch by identity, or null.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-switch entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The switch, or null.</returns>
    Task<SmartSwitch?> GetAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every managed switch for a server.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The managed switches for the server.</returns>
    Task<IReadOnlyList<SmartSwitch>> ListByServerAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default);

    /// <summary>True when a managed switch with this identity exists.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-switch entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if a matching switch exists.</returns>
    Task<bool> ExistsAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a switch (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-switch entity id.</param>
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
    /// <param name="entityId">The in-game smart-switch entity id.</param>
    /// <param name="messageId">The Discord embed message id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message id has been persisted.</returns>
    Task SetMessageIdAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        ulong messageId,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the last-known on/off state (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-switch entity id.</param>
    /// <param name="isActive">The last observed on/off state.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the state has been persisted.</returns>
    Task UpdateStateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool isActive,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a switch (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-switch entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the switch has been removed.</returns>
    Task RemoveAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken = default);
}
