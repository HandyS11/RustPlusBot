using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Gateway;

/// <summary>Primitive Discord operations the reconciler orchestrates. Implemented over Discord.Net
/// in production and by an in-memory fake in tests. Existence checks are cache reads (sync);
/// message existence and mutations are async REST calls.</summary>
internal interface IWorkspaceGateway
{
    /// <summary>True if a category with this snowflake currently exists in the guild.</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="categoryId">The snowflake ID of the category to check.</param>
    bool CategoryExists(ulong guildId, ulong categoryId);

    /// <summary>Finds a category by name, or null.</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="name">The name of the category to search for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The snowflake ID of the matching category, or null if not found.</returns>
    Task<ulong?> FindCategoryAsync(ulong guildId, string name, CancellationToken cancellationToken);

    /// <summary>Creates a category and returns its snowflake.</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="name">The name to assign to the new category.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The snowflake ID of the newly created category.</returns>
    Task<ulong> CreateCategoryAsync(ulong guildId, string name, CancellationToken cancellationToken);

    /// <summary>True if a text channel with this snowflake currently exists in the guild.</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="channelId">The snowflake ID of the channel to check.</param>
    bool ChannelExists(ulong guildId, ulong channelId);

    /// <summary>Finds a text channel by name under a category, or null.</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="categoryId">The snowflake ID of the parent category.</param>
    /// <param name="name">The name of the channel to search for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The snowflake ID of the matching channel, or null if not found.</returns>
    Task<ulong?> FindChannelAsync(ulong guildId, ulong categoryId, string name, CancellationToken cancellationToken);

    /// <summary>Creates a text channel under a category with the given profile; returns its snowflake.</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="categoryId">The snowflake ID of the parent category.</param>
    /// <param name="name">The name to assign to the new channel.</param>
    /// <param name="profile">The permission profile to apply to the channel.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The snowflake ID of the newly created channel.</returns>
    Task<ulong> CreateChannelAsync(ulong guildId,
        ulong categoryId,
        string name,
        ChannelPermissionProfile profile,
        CancellationToken cancellationToken);

    /// <summary>Re-applies parent + name + permission profile to an existing channel (adopt/heal path).</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="channelId">The snowflake ID of the channel to update.</param>
    /// <param name="categoryId">The snowflake ID of the parent category to set.</param>
    /// <param name="name">The name to set on the channel.</param>
    /// <param name="profile">The permission profile to re-apply.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ApplyChannelSettingsAsync(ulong guildId,
        ulong channelId,
        ulong categoryId,
        string name,
        ChannelPermissionProfile profile,
        CancellationToken cancellationToken);

    /// <summary>True if the message still exists in the channel.</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="channelId">The snowflake ID of the channel containing the message.</param>
    /// <param name="messageId">The snowflake ID of the message to check.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the message exists; otherwise false.</returns>
    Task<bool> MessageExistsAsync(ulong guildId, ulong channelId, ulong messageId, CancellationToken cancellationToken);

    /// <summary>Posts a new message and returns its snowflake.</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="channelId">The snowflake ID of the channel to post into.</param>
    /// <param name="payload">The content of the message to post.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The snowflake ID of the newly posted message.</returns>
    Task<ulong> PostMessageAsync(ulong guildId,
        ulong channelId,
        MessagePayload payload,
        CancellationToken cancellationToken);

    /// <summary>Edits an existing message in place.</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="channelId">The snowflake ID of the channel containing the message.</param>
    /// <param name="messageId">The snowflake ID of the message to edit.</param>
    /// <param name="payload">The new content to apply to the message.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="System.InvalidOperationException">The channel no longer exists in the guild.</exception>
    Task EditMessageAsync(ulong guildId,
        ulong channelId,
        ulong messageId,
        MessagePayload payload,
        CancellationToken cancellationToken);

    /// <summary>Deletes a single message; a no-op if it is already gone.</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="channelId">The snowflake ID of the channel containing the message.</param>
    /// <param name="messageId">The snowflake ID of the message to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteMessageAsync(ulong guildId, ulong channelId, ulong messageId, CancellationToken cancellationToken);

    /// <summary>Puts the given channels of a category into the given on-screen order. A cache-read
    /// no-op when they already are; otherwise one bulk reorder call that permutes the channels'
    /// existing position values, so channels outside the list keep their place.</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="categoryId">The snowflake ID of the category the channels live under.</param>
    /// <param name="orderedChannelIds">The channel snowflakes in desired top-to-bottom order.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task EnsureChannelOrderAsync(ulong guildId,
        ulong categoryId,
        IReadOnlyList<ulong> orderedChannelIds,
        CancellationToken cancellationToken);

    /// <summary>Deletes a channel by snowflake (no-op if already gone).</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="channelId">The snowflake ID of the channel to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteChannelAsync(ulong guildId, ulong channelId, CancellationToken cancellationToken);

    /// <summary>Deletes a category by snowflake (no-op if already gone).</summary>
    /// <param name="guildId">The snowflake ID of the guild.</param>
    /// <param name="categoryId">The snowflake ID of the category to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteCategoryAsync(ulong guildId, ulong categoryId, CancellationToken cancellationToken);

    /// <summary>Returns the names of required bot guild permissions that are missing (empty = all present).</summary>
    /// <param name="guildId">The snowflake ID of the guild to check permissions for.</param>
    IReadOnlyList<string> GetMissingBotPermissions(ulong guildId);
}
