namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Deletes all of a guild's data: provisioned channels plus its domain rows.</summary>
internal interface IGuildPurgeService
{
    /// <summary>Purges one guild back to a just-joined state (channels + servers + guild-scoped rows).</summary>
    /// <param name="guildId">The guild to purge.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the guild's data has been removed.</returns>
    Task PurgeGuildAsync(ulong guildId, CancellationToken cancellationToken = default);
}
