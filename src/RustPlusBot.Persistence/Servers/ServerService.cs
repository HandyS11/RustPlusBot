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
}
