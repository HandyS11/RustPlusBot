using RustPlusBot.Discord.Posting;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Render-gated in-place refresh of the three #info messages.</summary>
/// <param name="store">Supplies the provisioned message ids and the guild culture.</param>
/// <param name="gateway">Performs the Discord edit.</param>
/// <param name="renderers">All registered message renderers.</param>
/// <param name="gate">Suppresses PATCHes for renders that did not change.</param>
/// <param name="reconciler">The full reconcile, used to self-heal a missing or stale message.</param>
internal sealed class ServerInfoRefresher(
    IWorkspaceStore store,
    IWorkspaceGateway gateway,
    IEnumerable<IMessageRenderer> renderers,
    RenderGate gate,
    IWorkspaceReconciler reconciler) : IServerInfoRefresher
{
    /// <summary>The #info message keys this path refreshes, in declaration order.</summary>
    private static readonly string[] Keys =
    [
        WorkspaceMessageKeys.ServerInfo,
        WorkspaceMessageKeys.ServerEvents,
        WorkspaceMessageKeys.ServerTeam,
    ];

    private readonly Dictionary<string, IMessageRenderer> _renderers =
        renderers.ToDictionary(r => r.MessageKey, StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task RefreshAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var culture = await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        var context = new MessageRenderContext(guildId, serverId, culture);

        foreach (var key in Keys)
        {
            if (!_renderers.TryGetValue(key, out var renderer))
            {
                // The host did not compose the feature that owns this embed; nothing to refresh.
                continue;
            }

            var payload = await renderer.RenderAsync(context, cancellationToken).ConfigureAwait(false);
            if (payload.Text is null && payload.Embed is null && payload.Components is null)
            {
                // Nothing to show (e.g. the server row vanished mid-tick). Leave the message alone.
                continue;
            }

            var record = await store.GetMessageAsync(guildId, serverId, key, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                // Never posted (or wiped): the full reconcile owns creating it.
                await reconciler.ReconcileServerAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
                return;
            }

            var canonical = RenderCanonicalizer.Canonicalize(payload.Embed, payload.Components);
            if (!gate.ShouldSend(record.DiscordMessageId, canonical))
            {
                continue;
            }

            try
            {
                await gateway
                    .EditMessageAsync(guildId, record.DiscordChannelId, record.DiscordMessageId, payload,
                        cancellationToken)
                    .ConfigureAwait(false);
                gate.Commit(record.DiscordMessageId, canonical);
            }
            catch (OperationCanceledException)
            {
                throw; // Shutdown — let the loop unwind.
            }
#pragma warning disable CA1031 // Broad catch: any edit failure (deleted message, permissions, 5xx) heals the same way.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Never remember a render that did not land, or the next tick would skip it too.
                gate.Invalidate(record.DiscordMessageId);
                await reconciler.ReconcileServerAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
    }
}
