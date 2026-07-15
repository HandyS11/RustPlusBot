using Microsoft.EntityFrameworkCore;

namespace RustPlusBot.Persistence.Wipes;

/// <summary>Default <see cref="IWipeBaselineStore"/> over the RustServer baseline columns.</summary>
/// <param name="context">The bot database context.</param>
public sealed class WipeBaselineStore(BotDbContext context) : IWipeBaselineStore
{
    /// <inheritdoc />
    public async Task<WipeBaseline?> GetAsync(ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        var server = await context.RustServers
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.Id == serverId, cancellationToken)
            .ConfigureAwait(false);
        return server is null
            ? null
            : new WipeBaseline(server.LastWipeTimeUtc, server.LastMapSeed, server.LastMapSize);
    }

    /// <inheritdoc />
    public async Task SetAsync(ulong guildId,
        Guid serverId,
        WipeBaseline baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var server = await context.RustServers
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.Id == serverId, cancellationToken)
            .ConfigureAwait(false);
        if (server is null)
        {
            return;
        }

        server.LastWipeTimeUtc = baseline.WipeTimeUtc;
        server.LastMapSeed = baseline.MapSeed;
        server.LastMapSize = baseline.MapSize;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
