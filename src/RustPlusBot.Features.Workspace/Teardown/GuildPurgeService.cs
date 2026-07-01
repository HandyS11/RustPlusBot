using Microsoft.EntityFrameworkCore;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Purges a guild: tears down provisioned channels, then deletes its domain rows.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="servers">Server management (RemoveAsync cascades all per-server rows).</param>
/// <param name="teardown">Removes provisioned Discord channels/categories/messages.</param>
/// <param name="provisioningLock">Held across the whole purge to block concurrent reconciliation.</param>
internal sealed class GuildPurgeService(
    BotDbContext context,
    IServerService servers,
    WorkspaceTeardownService teardown,
    IProvisioningLock provisioningLock) : IGuildPurgeService
{
    /// <inheritdoc />
    public async Task PurgeGuildAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        // Hold the per-guild provisioning lock across the ENTIRE purge. Otherwise reconciliation could
        // interleave after the teardown step — in particular the self-heal that fires when teardown
        // deletes channels — and re-provision resources or add rows into a half-purged guild.
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);

        // 1) Delete provisioned Discord channels/categories/messages (Discord side + records). Use the
        //    lock-free core since we already hold the lock (ResetGuildAsync would deadlock re-acquiring).
        await teardown.ResetGuildCoreAsync(guildId, cancellationToken).ConfigureAwait(false);

        // 2) Remove each server; the RustServer FK cascade clears its per-server rows
        //    (connection state, command/map settings, switches, alarms, storage monitors, credentials).
        var known = await servers.ListAsync(guildId, cancellationToken).ConfigureAwait(false);
        foreach (var server in known)
        {
            await servers.RemoveAsync(guildId, server.Id, cancellationToken).ConfigureAwait(false);
        }

        // 3) Delete guild-keyed rows that have no cascade FK to RustServer (event subscriptions,
        //    paired entities, guild settings, FCM registrations).
        await context.EventSubscriptions.Where(e => e.GuildId == guildId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.PairedEntities.Where(p => p.GuildId == guildId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.GuildSettings.Where(g => g.GuildId == guildId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.FcmRegistrations.Where(f => f.GuildId == guildId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
