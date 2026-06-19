using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Servers;

/// <summary>Guild-scoped management of Rust+ server targets.</summary>
/// <param name="context">The bot database context.</param>
public sealed class ServerService(BotDbContext context) : IServerService
{
    /// <inheritdoc />
    public async Task<RustServer> AddAsync(
        ulong guildId,
        ulong addedByUserId,
        string name,
        string ip,
        int port,
        CancellationToken cancellationToken = default)
    {
        var server = new RustServer
        {
            GuildId = guildId,
            AddedByUserId = addedByUserId,
            Name = name,
            Ip = ip,
            Port = port,
        };

        context.RustServers.Add(server);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return server;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RustServer>> ListAsync(
        ulong guildId,
        CancellationToken cancellationToken = default) =>
        await context.RustServers
            .Where(s => s.GuildId == guildId)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<RustServer?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default) =>
        context.RustServers.SingleOrDefaultAsync(s => s.GuildId == guildId && s.Id == serverId, cancellationToken);

    /// <inheritdoc />
    public Task<RustServer?> GetByEndpointAsync(
        ulong guildId,
        string ip,
        int port,
        CancellationToken cancellationToken = default) =>
        context.RustServers
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.Ip == ip && s.Port == port, cancellationToken);

    /// <inheritdoc />
    public Task<RustServer?> GetByFacepunchServerIdAsync(
        ulong guildId,
        Guid facepunchServerId,
        CancellationToken cancellationToken = default) =>
        context.RustServers
            .SingleOrDefaultAsync(
                s => s.GuildId == guildId && s.FacepunchServerId == facepunchServerId, cancellationToken);

    /// <inheritdoc />
    public async Task SetFacepunchServerIdAsync(
        Guid serverId,
        Guid facepunchServerId,
        CancellationToken cancellationToken = default)
    {
        var server = await context.RustServers
            .SingleOrDefaultAsync(s => s.Id == serverId, cancellationToken)
            .ConfigureAwait(false);

        if (server is null || server.FacepunchServerId == facepunchServerId)
        {
            return;
        }

        server.FacepunchServerId = facepunchServerId;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        var server = await context.RustServers
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.Id == serverId, cancellationToken)
            .ConfigureAwait(false);

        if (server is null)
        {
            return false;
        }

        context.RustServers.Remove(server);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<(RustServer Server, bool Created)> ResolveOrCreateByEndpointAsync(
        ulong guildId,
        ulong addedByUserId,
        string name,
        string ip,
        int port,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.RustServers
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.Ip == ip && s.Port == port, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return (existing, false);
        }

        var server = new RustServer
        {
            GuildId = guildId,
            AddedByUserId = addedByUserId,
            Name = name,
            Ip = ip,
            Port = port,
        };
        context.RustServers.Add(server);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (server, true);
        }
        catch (DbUpdateException)
        {
            // Another pairing created the same endpoint between the read and the write; re-read the winner.
            context.Entry(server).State = EntityState.Detached;
            var winner = await context.RustServers
                .SingleAsync(s => s.GuildId == guildId && s.Ip == ip && s.Port == port, cancellationToken)
                .ConfigureAwait(false);
            return (winner, false);
        }
    }
}
