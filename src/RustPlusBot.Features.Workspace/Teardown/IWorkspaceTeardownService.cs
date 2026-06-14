namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Removes provisioned Discord resources and their records.</summary>
internal interface IWorkspaceTeardownService
{
    /// <summary>Deletes one server's category, channels, and records.</summary>
    /// <param name="guildId">The guild.</param>
    /// <param name="serverId">The server to remove.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task.</returns>
    Task RemoveServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the entire workspace for a guild (all scopes) and clears all records.</summary>
    /// <param name="guildId">The guild.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task.</returns>
    Task ResetGuildAsync(ulong guildId, CancellationToken cancellationToken = default);
}
