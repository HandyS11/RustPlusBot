using System.Globalization;
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Map;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>
/// Renders the per-server #info map message: the RustMaps monument-icon render once generated, else a
/// "preparing" placeholder. Always returns a non-empty embed — the reconciler skips an empty payload, and
/// this message must claim the top-of-channel slot on the very first reconcile.
/// </summary>
/// <param name="query">Live query seam (world size/seed).</param>
/// <param name="localizer">String resolution.</param>
/// <param name="readModel">
/// Read seam over the RustMaps generation state. Null when no RustMaps API key is configured — the message
/// then always shows the "preparing" placeholder.
/// </param>
internal sealed class ServerInfoMapMessageRenderer(
    IRustServerQuery query,
    ILocalizer localizer,
    IInfoMapReadModel? readModel = null) : IMessageRenderer
{
    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.ServerInfoMap;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ServerId is not Guid serverId)
        {
            return new MessagePayload(null, null, null);
        }

        var world = await query.GetWorldAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("map.info.title", context.Culture))
            .WithColor(Color.DarkGreen);

        InfoMapView? ready = null;
        if (world is not null)
        {
            embed
                .AddField(localizer.Get("map.info.size", context.Culture),
                    world.WorldSize.ToString(CultureInfo.InvariantCulture), inline: true)
                .AddField(localizer.Get("map.info.seed", context.Culture),
                    world.Seed.ToString(CultureInfo.InvariantCulture), inline: true);
            ready = readModel?.GetReady((int)world.WorldSize, (int)world.Seed);
        }

        if (ready is not null)
        {
            embed.WithImageUrl(ready.ImageUrl);
            if (ready.RustMapsPageUrl is not null)
            {
                embed.WithUrl(ready.RustMapsPageUrl);
            }
        }
        else
        {
            embed.WithFooter(localizer.Get("map.info.generating", context.Culture));
        }

        return new MessagePayload(null, embed.Build(), null);
    }
}
