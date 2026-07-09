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
    public async Task<bool> MessageExistsAsync(ulong guildId,
        ulong channelId,
        ulong messageId,
        CancellationToken cancellationToken)
    {
        var channel = client.GetGuild(guildId)?.GetTextChannel(channelId);
        if (channel is null)
        {
            return false;
        }

        var message = await channel.GetMessageAsync(messageId).ConfigureAwait(false);
        return message is not null;
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
        var message = await channel
            .SendMessageAsync(text: payload.Text, embed: payload.Embed, components: payload.Components)
            .ConfigureAwait(false);
        return message.Id;
    }

    /// <inheritdoc />
    public async Task EditMessageAsync(ulong guildId,
        ulong channelId,
        ulong messageId,
        MessagePayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var channel = client.GetGuild(guildId)?.GetTextChannel(channelId);
        if (channel is null)
        {
            return;
        }

        await channel.ModifyMessageAsync(messageId, props =>
        {
            props.Content = payload.Text;
            props.Embed = payload.Embed;
            props.Components = payload.Components;
        }).ConfigureAwait(false);
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
            await channel.DeleteMessageAsync(messageId, new RequestOptions
                {
                    CancelToken = cancellationToken
                })
                .ConfigureAwait(false);
        }
        catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone: best-effort delete succeeds trivially.
        }
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

    private static Task ApplyOverwritesAsync(SocketGuild guild, ITextChannel channel, ChannelPermissionProfile profile)
    {
        var send = profile == ChannelPermissionProfile.Interactive ? PermValue.Allow : PermValue.Deny;
        var overwrite = OverwritePermissions.InheritAll.Modify(viewChannel: PermValue.Allow, sendMessages: send);
        return channel.AddPermissionOverwriteAsync(guild.EveryoneRole, overwrite);
    }
}
