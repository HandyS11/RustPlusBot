using Discord;
using Discord.Net;
using Discord.WebSocket;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Gateway;

/// <summary>Discord.Net-backed <see cref="IWorkspaceGateway"/>.</summary>
/// <param name="client">The socket client (cache for existence checks; REST for mutations).</param>
internal sealed class DiscordWorkspaceGateway(DiscordSocketClient client) : IWorkspaceGateway
{
    /// <inheritdoc />
    public bool CategoryExists(ulong guildId, ulong categoryId) =>
        client.GetGuild(guildId)?.GetCategoryChannel(categoryId) is not null;

    /// <inheritdoc />
    public Task<ulong?> FindCategoryAsync(ulong guildId, string name, CancellationToken cancellationToken)
    {
        var match = client.GetGuild(guildId)?.CategoryChannels
            .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match?.Id);
    }

    /// <inheritdoc />
    public async Task<ulong> CreateCategoryAsync(ulong guildId, string name, CancellationToken cancellationToken)
    {
        var guild = GetGuild(guildId);
        var category = await guild.CreateCategoryChannelAsync(name).ConfigureAwait(false);
        return category.Id;
    }

    /// <inheritdoc />
    public bool ChannelExists(ulong guildId, ulong channelId) =>
        client.GetGuild(guildId)?.GetTextChannel(channelId) is not null;

    /// <inheritdoc />
    public Task<ulong?> FindChannelAsync(ulong guildId,
        ulong categoryId,
        string name,
        CancellationToken cancellationToken)
    {
        var match = client.GetGuild(guildId)?.TextChannels
            .FirstOrDefault(c =>
                c.CategoryId == categoryId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match?.Id);
    }

    /// <inheritdoc />
    public async Task<ulong> CreateChannelAsync(ulong guildId,
        ulong categoryId,
        string name,
        ChannelPermissionProfile profile,
        CancellationToken cancellationToken)
    {
        var guild = GetGuild(guildId);
        var channel = await guild.CreateTextChannelAsync(name, props => props.CategoryId = categoryId)
            .ConfigureAwait(false);
        await ApplyOverwritesAsync(guild, channel, profile).ConfigureAwait(false);
        return channel.Id;
    }

    /// <inheritdoc />
    public async Task ApplyChannelSettingsAsync(ulong guildId,
        ulong channelId,
        ulong categoryId,
        string name,
        ChannelPermissionProfile profile,
        CancellationToken cancellationToken)
    {
        var guild = client.GetGuild(guildId);
        var channel = guild?.GetTextChannel(channelId);
        if (guild is null || channel is null)
        {
            return;
        }

        if (channel.CategoryId != categoryId || !string.Equals(channel.Name, name, StringComparison.Ordinal))
        {
            await channel.ModifyAsync(props =>
            {
                props.CategoryId = categoryId;
                props.Name = name;
            }).ConfigureAwait(false);
        }

        await ApplyOverwritesAsync(guild, channel, profile).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LiveMessage?> GetLiveMessageAsync(ulong guildId,
        ulong channelId,
        ulong messageId,
        CancellationToken cancellationToken)
    {
        var channel = client.GetGuild(guildId)?.GetTextChannel(channelId);
        if (channel is null)
        {
            return null;
        }

        var message = await channel.GetMessageAsync(messageId, Options(cancellationToken)).ConfigureAwait(false);
        return message is null
            ? null
            : LiveMessage.From(message.Id, message.Attachments.FirstOrDefault()?.Filename,
                message.Embeds.FirstOrDefault()?.Image?.Url);
    }

    /// <inheritdoc />
    public async Task<ulong> PostMessageAsync(ulong guildId,
        ulong channelId,
        MessagePayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var channel = client.GetGuild(guildId)?.GetTextChannel(channelId)
                      ?? throw new InvalidOperationException($"Channel {channelId} not found in guild {guildId}.");
        if (payload.Attachment is not { } attachment)
        {
            var message = await channel
                .SendMessageAsync(text: payload.Text, embed: payload.Embed, options: Options(cancellationToken),
                    components: payload.Components)
                .ConfigureAwait(false);
            return message.Id;
        }

        var stream = new MemoryStream(attachment.Bytes);
        await using (stream.ConfigureAwait(false))
        {
            var message = await channel.SendFileAsync(stream, attachment.FileName, text: payload.Text,
                    embed: payload.Embed, options: Options(cancellationToken),
                    allowedMentions: AllowedMentions.None, components: payload.Components)
                .ConfigureAwait(false);
            return message.Id;
        }
    }

    /// <inheritdoc />
    public async Task EditMessageAsync(ulong guildId,
        ulong channelId,
        ulong messageId,
        MessagePayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var channel = client.GetGuild(guildId)?.GetTextChannel(channelId)
                      ?? throw new InvalidOperationException($"Channel {channelId} not found in guild {guildId}.");

        // Attachments are deliberately left unmentioned, which is what keeps them: Discord retains the
        // existing upload (and the embed's attachment:// reference to it) when an edit does not carry an
        // attachments field. The reconciler only edits a message whose upload already matches the payload,
        // so re-uploading here would burn its full size on every pass for no change.
        await channel.ModifyMessageAsync(messageId, props =>
        {
            props.Content = payload.Text;
            props.Embed = payload.Embed;
            props.Components = payload.Components;
        }, Options(cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteMessageAsync(ulong guildId,
        ulong channelId,
        ulong messageId,
        CancellationToken cancellationToken)
    {
        var channel = client.GetGuild(guildId)?.GetTextChannel(channelId);
        if (channel is null)
        {
            return;
        }

        try
        {
            await channel.DeleteMessageAsync(messageId, Options(cancellationToken))
                .ConfigureAwait(false);
        }
        catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone: best-effort delete succeeds trivially.
        }
    }

    /// <inheritdoc />
    public async Task EnsureChannelOrderAsync(ulong guildId,
        ulong categoryId,
        IReadOnlyList<ulong> orderedChannelIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedChannelIds);
        var guild = client.GetGuild(guildId);
        if (guild is null)
        {
            return;
        }

        var live = orderedChannelIds
            .Select(id => guild.GetTextChannel(id))
            .Where(c => c is not null && c.CategoryId == categoryId)
            .ToList();
        if (live.Count < 2)
        {
            return;
        }

        // Discord sorts a category's channels by position, ties by snowflake.
        var current = live.OrderBy(c => c.Position).ThenBy(c => c.Id).Select(c => c.Id);
        if (current.SequenceEqual(live.Select(c => c.Id)))
        {
            return;
        }

        // Permute the channels' existing position values rather than assigning fresh ones, so every
        // channel outside the list keeps its place relative to the reordered block.
        var slots = live.Select(c => c.Position).Order().ToList();
        await guild.ReorderChannelsAsync(
            live.Select((c, i) => new ReorderChannelProperties(c.Id, slots[i])),
            new RequestOptions
            {
                CancelToken = cancellationToken
            }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteChannelAsync(ulong guildId, ulong channelId, CancellationToken cancellationToken)
    {
        var channel = client.GetGuild(guildId)?.GetChannel(channelId);
        if (channel is not null)
        {
            await channel.DeleteAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteCategoryAsync(ulong guildId, ulong categoryId, CancellationToken cancellationToken)
    {
        var category = client.GetGuild(guildId)?.GetCategoryChannel(categoryId);
        if (category is not null)
        {
            await category.DeleteAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetMissingBotPermissions(ulong guildId)
    {
        var guild = client.GetGuild(guildId);
        if (guild is null)
        {
            return ["Guild not available to the bot"];
        }

        var permissions = guild.CurrentUser.GuildPermissions;
        var missing = new List<string>();
        if (!permissions.ManageChannels)
        {
            missing.Add("Manage Channels");
        }

        if (!permissions.ManageRoles)
        {
            missing.Add("Manage Roles");
        }

        if (!permissions.SendMessages)
        {
            missing.Add("Send Messages");
        }

        if (!permissions.EmbedLinks)
        {
            missing.Add("Embed Links");
        }

        if (!permissions.ManageMessages)
        {
            missing.Add("Manage Messages");
        }

        if (!permissions.ViewChannel)
        {
            missing.Add("View Channels");
        }

        return missing;
    }

    private SocketGuild GetGuild(ulong guildId) =>
        client.GetGuild(guildId) ?? throw new InvalidOperationException($"Guild {guildId} not available to the bot.");

    /// <summary>Wraps a token as Discord.Net request options, so REST calls unwind on shutdown.</summary>
    /// <param name="cancellationToken">The token to attach.</param>
    /// <returns>Request options carrying the token.</returns>
    private static RequestOptions Options(CancellationToken cancellationToken) =>
        new()
        {
            CancelToken = cancellationToken
        };

    private static Task ApplyOverwritesAsync(SocketGuild guild, ITextChannel channel, ChannelPermissionProfile profile)
    {
        var send = profile == ChannelPermissionProfile.Interactive ? PermValue.Allow : PermValue.Deny;
        var overwrite = OverwritePermissions.InheritAll.Modify(viewChannel: PermValue.Allow, sendMessages: send);
        return channel.AddPermissionOverwriteAsync(guild.EveryoneRole, overwrite);
    }
}
