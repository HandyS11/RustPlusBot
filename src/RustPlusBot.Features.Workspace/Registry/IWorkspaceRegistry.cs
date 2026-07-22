namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Aggregated, ordered view of all contributed channel/message specs.</summary>
internal interface IWorkspaceRegistry
{
    /// <summary>Channel specs for a scope, ordered by <see cref="ChannelSpec.Order"/>.</summary>
    /// <param name="scope">The scope to filter by.</param>
    /// <returns>The ordered channel specs.</returns>
    IReadOnlyList<ChannelSpec> GetChannelSpecs(WorkspaceScope scope);

    /// <summary>Message specs for a scope.</summary>
    /// <param name="scope">The scope to filter by.</param>
    /// <returns>The message specs.</returns>
    IReadOnlyList<MessageSpec> GetMessageSpecs(WorkspaceScope scope);

    /// <summary>
    /// Reports whether a named capability currently applies. An unknown capability — one with no
    /// registered provider — is unavailable, so a feature the host did not compose leaves no
    /// orphaned channels behind.
    /// </summary>
    /// <param name="capability">The capability name.</param>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id, or null for the global scope.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True when the capability's channels should exist.</returns>
    ValueTask<bool> IsCapabilityAvailableAsync(string capability,
        ulong guildId,
        Guid? serverId,
        CancellationToken cancellationToken);
}
