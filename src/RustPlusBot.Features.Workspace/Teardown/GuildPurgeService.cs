using Persistord.Core;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Purges a guild: tears down provisioned channels, then deletes its domain rows.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="servers">Server lookup, to know which connection loops to stop.</param>
/// <param name="teardown">Removes provisioned Discord channels/categories/messages.</param>
/// <param name="provisioningLock">Held across the whole purge to block concurrent reconciliation.</param>
/// <param name="connections">Stops each server's connection loop before its row is deleted.</param>
internal sealed class GuildPurgeService(
    BotDbContext context,
    IServerService servers,
    WorkspaceTeardownService teardown,
    IProvisioningLock provisioningLock,
    IServerConnectionStopper connections) : IGuildPurgeService
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

        // 2) Stop every connection loop BEFORE any row is deleted. A loop still running when its
        //    RustServer row goes away faults on the connection-state foreign key at its next status
        //    write, and leaks its socket for the life of the process. StopAsync joins the loop, so it
        //    is finished before the deletes land.
        var known = await servers.ListAsync(guildId, cancellationToken).ConfigureAwait(false);
        foreach (var serverId in known.Select(server => server.Id))
        {
            await connections.StopAsync(guildId, serverId).ConfigureAwait(false);
        }

        // 3) Delete every guild-scoped row in one transaction. Persistord walks the model for
        //    IGuildScoped entity types and deletes dependents before principals, so this covers both
        //    what used to cascade off RustServer and what never had a foreign key to it (guild
        //    settings, FCM registrations) — and a new guild-scoped table joins it by declaring the
        //    interface, rather than by someone remembering to add a line here.
        await context.PurgeGuildAsync(guildId, cancellationToken).ConfigureAwait(false);

        // The deletes run as SQL and leave the change tracker holding rows that no longer exist —
        // the server list above tracked some of them. The next SaveChanges on this scoped context
        // would try to flush them.
        context.ChangeTracker.Clear();
    }
}
