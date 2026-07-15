namespace RustPlusBot.Persistence.Wipes;

/// <summary>Reads/writes the per-server wipe baseline stored on the RustServer row.</summary>
public interface IWipeBaselineStore
{
    /// <summary>Gets the baseline, or null when the server row does not exist.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The baseline (all-null members before first observation), or null for an unknown server.</returns>
    Task<WipeBaseline?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Overwrites the baseline (no-op when the server row does not exist).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="baseline">The new baseline values.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the baseline has been persisted.</returns>
    Task SetAsync(ulong guildId, Guid serverId, WipeBaseline baseline, CancellationToken cancellationToken = default);
}
