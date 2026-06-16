using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Commands;

namespace RustPlusBot.Persistence.Commands;

/// <summary>EF-backed <see cref="IMuteStore"/>.</summary>
/// <param name="context">The bot database context.</param>
public sealed class MuteStore(BotDbContext context) : IMuteStore
{
    /// <inheritdoc />
    public async Task<bool> GetMutedAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default)
    {
        var row = await context.ServerCommandSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.ServerId == serverId, cancellationToken)
            .ConfigureAwait(false);
        return row?.Muted ?? false;
    }

    /// <inheritdoc />
    public async Task SetMutedAsync(ulong guildId,
        Guid serverId,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.ServerCommandSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.ServerId == serverId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.ServerCommandSettings.Add(new ServerCommandSettings
            {
                GuildId = guildId, ServerId = serverId, Muted = muted,
            });
        }
        else
        {
            existing.Muted = muted;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> GetPrefixAsync(ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        var row = await context.ServerCommandSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.ServerId == serverId, cancellationToken)
            .ConfigureAwait(false);
        return row?.Prefix ?? "!";
    }
}
