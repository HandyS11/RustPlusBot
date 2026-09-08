using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.Messages;

/// <summary>
///     The prologue every anchored #claninfo renderer shares: reject a serverless context, load the
///     server's clan snapshot, and short-circuit when there is no clan to describe.
/// </summary>
internal static class ClanMessageShell
{
    /// <summary>
    ///     Loads the clan snapshot for <paramref name="context"/> and hands it to <paramref name="render"/>,
    ///     or returns an inert payload when the context has no server or the server has no clan.
    /// </summary>
    /// <param name="store">Supplies the stored clan snapshot.</param>
    /// <param name="context">The render context.</param>
    /// <param name="render">Builds the payload from the loaded snapshot, its server id and the culture.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The rendered payload, or an inert one.</returns>
    public static async ValueTask<MessagePayload> RenderAsync(
        IClanStore store,
        MessageRenderContext context,
        Func<ClanSnapshot, Guid, string, ValueTask<MessagePayload>> render,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ServerId is not Guid serverId)
        {
            return Inert;
        }

        var clan = await store.GetAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);

        // The clan channel only exists while a clan does, so there is no provisioned message to edit
        // here. An empty payload keeps these keys inert on clanless servers.
        return clan is null
            ? Inert
            : await render(clan, serverId, context.Culture).ConfigureAwait(false);
    }

    /// <summary>The payload that leaves whatever is on screen untouched.</summary>
    private static MessagePayload Inert => new(null, null, null);
}
