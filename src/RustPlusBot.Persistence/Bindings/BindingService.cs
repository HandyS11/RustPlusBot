using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Guilds;

namespace RustPlusBot.Persistence.Bindings;

/// <summary>Guild-scoped management of channel-to-feature bindings.</summary>
/// <param name="context">The bot database context.</param>
public sealed class BindingService(BotDbContext context) : IBindingService
{
    /// <inheritdoc />
    public async Task BindAsync(
        ulong guildId,
        BoundFeature feature,
        ulong channelId,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.ChannelBindings
            .SingleOrDefaultAsync(b => b.GuildId == guildId && b.Feature == feature, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.ChannelBindings.Add(new ChannelBinding
            {
                GuildId = guildId,
                Feature = feature,
                ChannelId = channelId,
            });
        }
        else
        {
            existing.ChannelId = channelId;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ulong?> GetBoundChannelAsync(
        ulong guildId,
        BoundFeature feature,
        CancellationToken cancellationToken = default)
    {
        var binding = await context.ChannelBindings
            .SingleOrDefaultAsync(b => b.GuildId == guildId && b.Feature == feature, cancellationToken)
            .ConfigureAwait(false);

        return binding?.ChannelId;
    }
}
