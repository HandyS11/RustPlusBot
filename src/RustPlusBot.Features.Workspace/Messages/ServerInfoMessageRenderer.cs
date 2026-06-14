using System.Globalization;
using Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders a server's #info identity embed (static in 1a; live status enriched in 1b).</summary>
/// <param name="servers">Server lookup.</param>
/// <param name="localizer">String resolution.</param>
internal sealed class ServerInfoMessageRenderer(IServerService servers, ILocalizer localizer) : IMessageRenderer
{
    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.ServerInfo;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ServerId is not Guid serverId)
        {
            return new MessagePayload(null, null, null);
        }

        var server = await servers.GetAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            return new MessagePayload(null, null, null);
        }

        var port = server.Port.ToString(CultureInfo.InvariantCulture);
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("server.info.title", context.Culture, server.Name))
            .WithDescription(localizer.Get("server.info.endpoint", context.Culture, server.Ip, port))
            .WithColor(Color.Green)
            .Build();
        return new MessagePayload(null, embed, null);
    }
}
