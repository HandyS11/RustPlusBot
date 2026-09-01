using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Deletes provisioned resources under the per-guild lock, then clears the records.</summary>
/// <param name="gateway">Discord operations.</param>
/// <param name="store">Provisioning persistence.</param>
/// <param name="provisioningLock">Per-guild serialization (shared with the reconciler).</param>
internal sealed class WorkspaceTeardownService(
    IWorkspaceGateway gateway,
    IWorkspaceStore store,
    IProvisioningLock provisioningLock) : IWorkspaceTeardownService
{
    /// <inheritdoc />
    public async Task RemoveServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default)
    {
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);
        await RemoveServerCoreAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResetGuildAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);
        await ResetGuildCoreAsync(guildId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes one server's provisioned resources and records WITHOUT taking the provisioning lock.
    /// The caller MUST already hold the guild's <see cref="IProvisioningLock"/> — <see cref="ServerPurgeService"/>
    /// uses this so it can hold the lock across the row delete too. External callers use
    /// <see cref="RemoveServerAsync"/>.
    /// </summary>
    /// <param name="guildId">The guild.</param>
    /// <param name="serverId">The server to remove.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task.</returns>
    internal Task RemoveServerCoreAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default) =>
        DeleteScopeAsync(guildId, serverId, cancellationToken);

    /// <summary>
    /// Deletes the guild's provisioned resources and records WITHOUT taking the provisioning lock.
    /// The caller MUST already hold the guild's <see cref="IProvisioningLock"/> — <see cref="GuildPurgeService"/>
    /// uses this so it can hold the lock across the wider purge. External callers use <see cref="ResetGuildAsync"/>.
    /// </summary>
    /// <param name="guildId">The guild to reset.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task.</returns>
    internal async Task ResetGuildCoreAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var categories = await store.GetAllCategoriesAsync(guildId, cancellationToken).ConfigureAwait(false);
        foreach (var category in categories)
        {
            await DeleteScopeAsync(guildId, category.RustServerId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DeleteScopeAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken)
    {
        foreach (var channel in await store.GetChannelsAsync(guildId, serverId, cancellationToken)
                     .ConfigureAwait(false))
        {
            await gateway.DeleteChannelAsync(guildId, channel.DiscordChannelId, cancellationToken)
                .ConfigureAwait(false);
        }

        var category = await store.GetCategoryAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (category is not null)
        {
            await gateway.DeleteCategoryAsync(guildId, category.DiscordCategoryId, cancellationToken)
                .ConfigureAwait(false);
        }

        await store.DeleteScopeAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
    }
}
