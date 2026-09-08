using System.Globalization;
using System.Security.Cryptography;
using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Map;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>
/// Renders the per-server #info map message: the RustMaps monument-icon render once it is generated AND
/// verified to depict this server's world. A seed and a size do not identify a Rust world — a server on a
/// pre-generated or edited level still reports the seed from its config — so an unverified render can be a
/// different island entirely; for those servers the map Rust+ itself serves is attached instead. Until the
/// verdict is in it returns an EMPTY payload so the reconciler leaves any existing map message untouched
/// (preserving the last image instead of overwriting it with a placeholder on every boot). The message is
/// (re)positioned above the status embed by the reconciler's channel-order repair when it first becomes
/// non-empty, so it does not need to be non-empty on the very first reconcile.
/// </summary>
/// <param name="query">Live query seam (world size/seed, and the server's own map image).</param>
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
        if (world is null || readModel is null)
        {
            return Empty;
        }

        var resolution = readModel.Resolve(context.GuildId, serverId, (int)world.WorldSize, (int)world.Seed);
        return resolution.Status switch
        {
            InfoMapStatus.Verified when resolution.View is { } view => RenderRustMaps(context, world, view),
            InfoMapStatus.Mismatched => await RenderServerMapAsync(context, serverId, world, cancellationToken)
                .ConfigureAwait(false),

            // Not decided yet: return nothing so the reconciler leaves any existing map message (with its
            // last image) untouched, rather than overwriting it with a placeholder on every boot.
            _ => Empty,
        };
    }

    private static EmbedBuilder BaseEmbed(MessageRenderContext context, ILocalizer localizer, WorldSnapshot world) =>
        new EmbedBuilder()
            .WithTitle(localizer.Get("map.info.title", context.Culture))
            .WithColor(Color.DarkGreen)
            .AddField(localizer.Get("map.info.size", context.Culture),
                world.WorldSize.ToString(CultureInfo.InvariantCulture), inline: true)
            .AddField(localizer.Get("map.info.seed", context.Culture),
                world.Seed.ToString(CultureInfo.InvariantCulture), inline: true);

    /// <summary>Names the upload after its content, so the reconciler can tell "already posted" from "changed".</summary>
    /// <param name="bytes">The image bytes.</param>
    /// <returns>A stable file name for these bytes.</returns>
    private static string FileNameFor(byte[] bytes) =>
        $"server-map-{Convert.ToHexStringLower(SHA256.HashData(bytes))[..12]}.jpg";

    private MessagePayload RenderRustMaps(MessageRenderContext context, WorldSnapshot world, InfoMapView view)
    {
        var embed = BaseEmbed(context, localizer, world).WithImageUrl(view.ImageUrl);
        if (view.RustMapsPageUrl is not null)
        {
            embed.WithUrl(view.RustMapsPageUrl);
        }

        return new MessagePayload(null, embed.Build(), null);
    }

    /// <summary>
    /// The RustMaps render is for a different world, so the only truthful image is the one the server serves
    /// over Rust+. It is uploaded with the message and shown through <c>attachment://</c>; the file name is
    /// content-derived, so the reconciler re-posts it exactly when the server's map actually changes.
    /// </summary>
    /// <param name="context">The render context.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="world">The world snapshot (size and seed).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The payload, or empty when the server's map image is unavailable.</returns>
    private async Task<MessagePayload> RenderServerMapAsync(
        MessageRenderContext context,
        Guid serverId,
        WorldSnapshot world,
        CancellationToken cancellationToken)
    {
        var image = await query.GetMapImageAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (image is not { Length: > 0 })
        {
            return Empty;
        }

        var fileName = FileNameFor(image);
        var embed = BaseEmbed(context, localizer, world)
            .WithImageUrl($"attachment://{fileName}")
            .WithFooter(localizer.Get("map.info.custom", context.Culture))
            .Build();
        return new MessagePayload(null, embed, null, new MessageAttachment(image, fileName));
    }
}
