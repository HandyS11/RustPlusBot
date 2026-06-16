namespace RustPlusBot.Persistence.Commands;

/// <summary>Reads/writes per-(guild, server) command settings: mute state and trigger prefix.</summary>
public interface IMuteStore
{
    /// <summary>Gets whether bot-to-game output is muted (false when unset).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if muted; false when unset.</returns>
    Task<bool> GetMutedAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Sets the mute state, creating the row if needed.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="muted">The mute state to persist.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the state has been persisted.</returns>
    Task SetMutedAsync(ulong guildId, Guid serverId, bool muted, CancellationToken cancellationToken = default);

    /// <summary>Gets the command prefix ("!" when unset).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The trigger prefix, or "!" when unset.</returns>
    Task<string> GetPrefixAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
}
