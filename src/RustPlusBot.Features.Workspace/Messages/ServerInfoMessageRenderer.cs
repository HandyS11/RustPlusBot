using System.Globalization;
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders a server's #info status embed: connection, population, in-game time and wipe age.</summary>
/// <param name="servers">Server lookup.</param>
/// <param name="connections">Live connection state + pool.</param>
/// <param name="query">Live server query.</param>
/// <param name="clock">Supplies the current time for the wipe age.</param>
/// <param name="localizer">String resolution.</param>
internal sealed class ServerInfoMessageRenderer(
    IServerService servers,
    IConnectionStore connections,
    IRustServerQuery query,
    IClock clock,
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

        if (status == ConnectionStatus.Connected)
        {
            await AddLiveFieldsAsync(embed, context, serverId, cancellationToken).ConfigureAwait(false);
        }

        // The swap select moved to /server player; the remove button now owns row 0 alone.
        var builder = new ComponentBuilder()
            .WithButton(
                localizer.Get("server.info.remove.button", context.Culture),
                $"{WorkspaceComponentIds.ServerInfoRemovePrefix}{serverId}",
                ButtonStyle.Danger,
                row: 0);

        return new MessagePayload(null, embed.Build(), builder.Build());
    }

    private async ValueTask AddLiveFieldsAsync(
        EmbedBuilder embed,
        MessageRenderContext context,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        var info = await query.GetServerInfoAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (info is not null)
        {
            embed.AddField(
                localizer.Get("server.info.players.label", context.Culture),
                localizer.Get("server.info.players.value", context.Culture,
                    info.Players.ToString(CultureInfo.InvariantCulture),
                    info.MaxPlayers.ToString(CultureInfo.InvariantCulture),
                    info.QueuedPlayers.ToString(CultureInfo.InvariantCulture)));
        }

        var time = await query.GetTimeAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (time is not null)
        {
            var isDay = Daylight.IsDay(time);
            embed.AddField(
                localizer.Get("server.info.time.label", context.Culture),
                localizer.Get("server.info.time.value", context.Culture,
                    Daylight.Clock(time),
                    localizer.Get(isDay ? "server.info.time.day" : "server.info.time.night", context.Culture),
                    DurationFormat.Compact(Daylight.UntilTransition(time)),
                    localizer.Get(isDay ? "server.info.time.to.night" : "server.info.time.to.day", context.Culture)));
        }

        if (info is not null)
        {
            embed.AddField(
                localizer.Get("server.info.wipe.label", context.Culture),
                info.WipeTimeUtc is { } wiped
                    ? localizer.Get("server.info.wipe.value", context.Culture,
                        DurationFormat.Compact(clock.UtcNow - wiped))
                    : localizer.Get("server.info.wipe.unknown", context.Culture));
        }
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
