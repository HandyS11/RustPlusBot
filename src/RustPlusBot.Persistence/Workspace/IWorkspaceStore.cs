using RustPlusBot.Domain.Workspace;

namespace RustPlusBot.Persistence.Workspace;

/// <summary>Persistence for the bot's provisioned categories, channels, and anchored messages.</summary>
public interface IWorkspaceStore
{
    /// <summary>Gets the category for a scope (<paramref name="serverId"/> null = global), or null.</summary>
    /// <param name="guildId">The Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id, or null for the global scope.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching category, or null if none exists.</returns>
    Task<ProvisionedCategory?> GetCategoryAsync(ulong guildId,
        Guid? serverId,
        CancellationToken cancellationToken = default);

    /// <summary>Upserts the category for its scope (keyed by guild + server).</summary>
    /// <param name="category">The category to save.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task SaveCategoryAsync(ProvisionedCategory category, CancellationToken cancellationToken = default);

    /// <summary>Gets all provisioned channels for a scope.</summary>
    /// <param name="guildId">The Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id, or null for the global scope.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The provisioned channels for the scope.</returns>
    Task<IReadOnlyList<ProvisionedChannel>> GetChannelsAsync(ulong guildId,
        Guid? serverId,
        CancellationToken cancellationToken = default);

    /// <summary>Upserts a channel for its scope (keyed by guild + server + channel key).</summary>
    /// <param name="channel">The channel to save.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task SaveChannelAsync(ProvisionedChannel channel, CancellationToken cancellationToken = default);

    /// <summary>Gets the anchored message for a scope + message key, or null.</summary>
    /// <param name="guildId">The Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id, or null for the global scope.</param>
    /// <param name="messageKey">The stable spec key (e.g. "information.main").</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching message, or null if none exists.</returns>
    Task<ProvisionedMessage?> GetMessageAsync(ulong guildId,
        Guid? serverId,
        string messageKey,
        CancellationToken cancellationToken = default);

    /// <summary>Upserts an anchored message (keyed by guild + server + message key).</summary>
    /// <param name="message">The message to save.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task SaveMessageAsync(ProvisionedMessage message, CancellationToken cancellationToken = default);

    /// <summary>Gets a guild's BCP-47 culture, defaulting to "en".</summary>
    /// <param name="guildId">The Discord guild snowflake.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The guild's culture tag, or "en" if not set.</returns>
    Task<string> GetCultureAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Sets a guild's culture (upserting the GuildSettings row).</summary>
    /// <param name="guildId">The Discord guild snowflake.</param>
    /// <param name="culture">The BCP-47 culture tag to set.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task SetCultureAsync(ulong guildId, string culture, CancellationToken cancellationToken = default);

    /// <summary>True when the guild wants the server-wiped announcement to ping @everyone. Defaults to false.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The current flag value.</returns>
    Task<bool> GetPingEveryoneOnWipeAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Sets the @everyone-on-wipe flag (upserting the GuildSettings row).</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="enabled">The new flag value.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the flag has been persisted.</returns>
    Task SetPingEveryoneOnWipeAsync(ulong guildId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Deletes all provisioning rows (category + channels + messages) for one scope.</summary>
    /// <param name="guildId">The Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id, or null for the global scope.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task DeleteScopeAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default);

    /// <summary>Gets every provisioned category in a guild (all scopes).</summary>
    /// <param name="guildId">The Discord guild snowflake.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>All provisioned categories for the guild.</returns>
    Task<IReadOnlyList<ProvisionedCategory>> GetAllCategoriesAsync(ulong guildId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the distinct guild ids that have any provisioned category (for startup reconcile).</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The distinct guild snowflakes that have at least one provisioned category.</returns>
    Task<IReadOnlyList<ulong>> GetProvisionedGuildIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets every provisioned channel with the given key across all guilds and scopes.</summary>
    /// <param name="channelKey">The stable channel key (e.g. "teamchat").</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>All provisioned channels with that key.</returns>
    Task<IReadOnlyList<ProvisionedChannel>> GetChannelsByKeyAsync(
        string channelKey,
        CancellationToken cancellationToken = default);
}
