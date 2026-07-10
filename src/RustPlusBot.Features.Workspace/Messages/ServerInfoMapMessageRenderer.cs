using System.Globalization;
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Map;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>
/// Renders the per-server #info map message: the RustMaps monument-icon render once generated. Until the
/// render is ready it returns an EMPTY payload so the reconciler leaves any existing map message untouched
/// (preserving the last image instead of overwriting it with a placeholder on every boot). The message is
/// (re)positioned above the status embed by the reconciler's channel-order repair when it first becomes
/// non-empty, so it does not need to be non-empty on the very first reconcile.
/// </summary>
/// <param name="query">Live query seam (world size/seed).</param>
/// <param name="localizer">String resolution.</param>
/// <param name="readModel">
/// Read seam over the RustMaps generation state. Null when no RustMaps API key is configured — the message
/// then never has a render to show, and the empty payload leaves the channel untouched.
/// </param>
internal sealed class ServerInfoMapMessageRenderer(
    IRustServerQuery query,
    ILocalizer localizer,
    IInfoMapReadModel? readModel = null) : IMessageRenderer
{
    private static readonly MessagePayload Empty = new(null, null, null);

    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.ServerInfoMap;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ServerId is not Guid serverId)
        {
            return Empty;
        }

        var world = await query.GetWorldAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var ready = world is not null
            ? readModel?.GetReady((int)world.WorldSize, (int)world.Seed)
            : null;
        if (ready is null)
        {
            // Not generated yet: return nothing so the reconciler leaves any existing map message (with its
            // last image) untouched, rather than overwriting it with a placeholder on every boot.
            return Empty;
        }

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("map.info.title", context.Culture))
            .WithColor(Color.DarkGreen)
            .AddField(localizer.Get("map.info.size", context.Culture),
                world!.WorldSize.ToString(CultureInfo.InvariantCulture), inline: true)
            .AddField(localizer.Get("map.info.seed", context.Culture),
                world.Seed.ToString(CultureInfo.InvariantCulture), inline: true)
            .WithImageUrl(ready.ImageUrl);
        if (ready.RustMapsPageUrl is not null)
        {
            embed.WithUrl(ready.RustMapsPageUrl);
        }

        return new MessagePayload(null, embed.Build(), null);
    }
}
