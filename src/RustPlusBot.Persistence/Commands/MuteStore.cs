using Microsoft.EntityFrameworkCore;
using Persistord.Core;
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
        CancellationToken cancellationToken = default) =>
        await context.ServerCommandSettings.UpsertAsync(
                s => s.GuildId == guildId && s.ServerId == serverId,
                () => new ServerCommandSettings
                {
                    GuildId = guildId, ServerId = serverId
                },
                row => row.Muted = muted,
                cancellationToken)
            .ConfigureAwait(false);

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
