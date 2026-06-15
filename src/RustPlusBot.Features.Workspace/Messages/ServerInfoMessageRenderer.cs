using System.Globalization;
using Discord;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders a server's #info embed with live connection status and the ManageGuild swap select.</summary>
/// <param name="servers">Server lookup.</param>
/// <param name="connections">Live connection state + pool.</param>
/// <param name="localizer">String resolution.</param>
internal sealed class ServerInfoMessageRenderer(
    IServerService servers,
    IConnectionStore connections,
    ILocalizer localizer) : IMessageRenderer
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

        var state = await connections.GetStateAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var pool = await connections.ListPoolAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var status = state?.Status ?? ConnectionStatus.NoCredentials;

        var active = pool.FirstOrDefault(c => state?.ActiveCredentialId is Guid id && c.Id == id)
                     ?? pool.FirstOrDefault(c => c.Status == CredentialStatus.Active);
        var none = localizer.Get("server.info.none", context.Culture);
        var port = server.Port.ToString(CultureInfo.InvariantCulture);

        var embed = new EmbedBuilder()
            .WithTitle($"{Glyph(status)} {server.Name}")
            .WithDescription(localizer.Get("server.info.endpoint", context.Culture, server.Ip, port))
            .WithColor(ColorFor(status))
            .AddField(localizer.Get("server.info.status.label", context.Culture), StatusText(status, context.Culture))
            .AddField(localizer.Get("server.info.player.label", context.Culture),
                active is null ? none : active.SteamId.ToString(CultureInfo.InvariantCulture));

        if (status == ConnectionStatus.Connected && state?.PlayerCount is int count)
        {
            embed.AddField(localizer.Get("server.info.players.label", context.Culture),
                count.ToString(CultureInfo.InvariantCulture));
        }

        var eligible = pool
            .Where(c => c.Status != CredentialStatus.Invalid)
            .Take(SelectMenuBuilder.MaxOptionCount) // Discord hard-limits a select menu to 25 options.
            .ToList();
        var builder = new ComponentBuilder();
        if (eligible.Count > 0)
        {
            var select = new SelectMenuBuilder()
                .WithCustomId($"{WorkspaceComponentIds.ServerInfoSwapPrefix}{serverId}")
                .WithPlaceholder(localizer.Get("server.info.swap.placeholder", context.Culture));
            foreach (var credential in eligible)
            {
                var label = credential.SteamId.ToString(CultureInfo.InvariantCulture);
                select.AddOption(label, credential.Id.ToString(), isDefault: credential.Id == active?.Id);
            }

            builder.WithSelectMenu(select, row: 0);
        }

        // Keep the remove button on its own row: Discord rejects an action row that mixes a select with buttons.
        builder.WithButton(
            localizer.Get("server.info.remove.button", context.Culture),
            $"{WorkspaceComponentIds.ServerInfoRemovePrefix}{serverId}",
            ButtonStyle.Danger,
            row: eligible.Count > 0 ? 1 : 0);

        return new MessagePayload(null, embed.Build(), builder.Build());
    }

    private static string Glyph(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => "🟢",
        ConnectionStatus.Connecting or ConnectionStatus.Unreachable => "🟡",
        _ => "🔴",
    };

    private static Color ColorFor(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => Color.Green,
        ConnectionStatus.Connecting or ConnectionStatus.Unreachable => Color.Gold,
        _ => Color.Red,
    };

    private string StatusText(ConnectionStatus status, string culture) => status switch
    {
        ConnectionStatus.Connecting => localizer.Get("server.info.status.connecting", culture),
        ConnectionStatus.Connected => localizer.Get("server.info.status.connected", culture),
        ConnectionStatus.Unreachable => localizer.Get("server.info.status.unreachable", culture),
        _ => localizer.Get("server.info.status.nocredentials", culture),
    };
}
