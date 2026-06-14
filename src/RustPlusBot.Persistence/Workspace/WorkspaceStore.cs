using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Workspace;

namespace RustPlusBot.Persistence.Workspace;

/// <summary>EF Core implementation of <see cref="IWorkspaceStore"/> over <see cref="BotDbContext"/>.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="clock">The clock used for create/update timestamps.</param>
public sealed class WorkspaceStore(BotDbContext context, IClock clock) : IWorkspaceStore
{
    /// <inheritdoc />
    public Task<ProvisionedCategory?> GetCategoryAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default) =>
        context.ProvisionedCategories
            .SingleOrDefaultAsync(c => c.GuildId == guildId && c.RustServerId == serverId, cancellationToken);

    /// <inheritdoc />
    public async Task SaveCategoryAsync(ProvisionedCategory category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        var existing = await context.ProvisionedCategories
            .SingleOrDefaultAsync(c => c.GuildId == category.GuildId && c.RustServerId == category.RustServerId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            category.CreatedAt = clock.UtcNow;
            context.ProvisionedCategories.Add(category);
        }
        else
        {
            existing.DiscordCategoryId = category.DiscordCategoryId;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProvisionedChannel>> GetChannelsAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default) =>
        await context.ProvisionedChannels
            .Where(c => c.GuildId == guildId && c.RustServerId == serverId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SaveChannelAsync(ProvisionedChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var existing = await context.ProvisionedChannels
            .SingleOrDefaultAsync(
                c => c.GuildId == channel.GuildId && c.RustServerId == channel.RustServerId && c.ChannelKey == channel.ChannelKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            channel.CreatedAt = clock.UtcNow;
            context.ProvisionedChannels.Add(channel);
        }
        else
        {
            existing.DiscordChannelId = channel.DiscordChannelId;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ProvisionedMessage?> GetMessageAsync(ulong guildId, Guid? serverId, string messageKey, CancellationToken cancellationToken = default) =>
        context.ProvisionedMessages
            .SingleOrDefaultAsync(m => m.GuildId == guildId && m.RustServerId == serverId && m.MessageKey == messageKey, cancellationToken);

    /// <inheritdoc />
    public async Task SaveMessageAsync(ProvisionedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var existing = await context.ProvisionedMessages
            .SingleOrDefaultAsync(
                m => m.GuildId == message.GuildId && m.RustServerId == message.RustServerId && m.MessageKey == message.MessageKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            message.CreatedAt = clock.UtcNow;
            message.UpdatedAt = clock.UtcNow;
            context.ProvisionedMessages.Add(message);
        }
        else
        {
            existing.DiscordChannelId = message.DiscordChannelId;
            existing.DiscordMessageId = message.DiscordMessageId;
            existing.UpdatedAt = clock.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> GetCultureAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var settings = await context.GuildSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId, cancellationToken)
            .ConfigureAwait(false);
        return settings?.Culture ?? "en";
    }

    /// <inheritdoc />
    public async Task SetCultureAsync(ulong guildId, string culture, CancellationToken cancellationToken = default)
    {
        var settings = await context.GuildSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId, cancellationToken)
            .ConfigureAwait(false);

        if (settings is null)
        {
            context.GuildSettings.Add(new GuildSettings { GuildId = guildId, Culture = culture });
        }
        else
        {
            settings.Culture = culture;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteScopeAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default)
    {
        await context.ProvisionedMessages
            .Where(m => m.GuildId == guildId && m.RustServerId == serverId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.ProvisionedChannels
            .Where(c => c.GuildId == guildId && c.RustServerId == serverId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.ProvisionedCategories
            .Where(c => c.GuildId == guildId && c.RustServerId == serverId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProvisionedCategory>> GetAllCategoriesAsync(ulong guildId, CancellationToken cancellationToken = default) =>
        await context.ProvisionedCategories
            .Where(c => c.GuildId == guildId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ulong>> GetProvisionedGuildIdsAsync(CancellationToken cancellationToken = default) =>
        await context.ProvisionedCategories
            .Select(c => c.GuildId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
