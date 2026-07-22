using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Clans.Writing;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Clans.Modules;

/// <summary>
/// Thin handler for the clan Set MOTD button and modal. Any guild member may open the modal; the
/// in-game clan permission is enforced server-side by <see cref="IClanMotdWriter"/>, not here.
/// </summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="localizer">Resolves localized reply strings.</param>
public sealed class ClanMotdModule(IServiceScopeFactory scopeFactory, ILocalizer localizer)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string InvalidControlMessage = "That control wasn't valid.";

    /// <summary>Opens the Set MOTD modal, carrying the target server id in the modal custom id.</summary>
    /// <param name="tail">The server id (from <see cref="ClanComponentIds.SetMotdButtonPrefix"/>'s custom id).</param>
    [ComponentInteraction(ClanComponentIds.SetMotdButtonPrefix + "*")]
    public async Task OpenAsync(string tail)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await RespondWithModalAsync<ClanMotdModal>(ClanComponentIds.SetMotdModalPrefix + tail)
            .ConfigureAwait(false);
    }

    /// <summary>Applies the new MOTD via <see cref="IClanMotdWriter"/>, then refreshes the overview embed.</summary>
    /// <param name="tail">The server id (from <see cref="ClanComponentIds.SetMotdModalPrefix"/>'s custom id).</param>
    /// <param name="modal">The submitted MOTD modal.</param>
    [ModalInteraction(ClanComponentIds.SetMotdModalPrefix + "*")]
    public async Task SubmitAsync(string tail, ClanMotdModal modal)
    {
        ArgumentNullException.ThrowIfNull(modal);
        if (!Guid.TryParse(tail, out var serverId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var guildId = Context.Guild.Id;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var culture = await scope.ServiceProvider.GetRequiredService<IWorkspaceStore>()
                .GetCultureAsync(guildId, CancellationToken.None).ConfigureAwait(false);

            var credential = await scope.ServiceProvider.GetRequiredService<IConnectionStore>()
                .GetActiveCredentialAsync(guildId, serverId, CancellationToken.None).ConfigureAwait(false);
            if (credential is null)
            {
                await FollowupAsync(localizer.Get("clan.motd.notpermitted", culture), ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            var writer = scope.ServiceProvider.GetRequiredService<IClanMotdWriter>();
            var result = await writer
                .SetAsync(guildId, serverId, credential.SteamId, modal.Motd, CancellationToken.None)
                .ConfigureAwait(false);

            if (result == ClanMotdWriteResult.Ok)
            {
                var refresher = scope.ServiceProvider.GetRequiredService<IServerInfoRefresher>();
                await refresher.RefreshAsync(guildId, serverId, CancellationToken.None).ConfigureAwait(false);
            }

            await FollowupAsync(Describe(result, culture), ephemeral: true).ConfigureAwait(false);
        }
    }

    private string Describe(ClanMotdWriteResult result, string culture) => result switch
    {
        ClanMotdWriteResult.Ok => localizer.Get("clan.motd.ok", culture),
        ClanMotdWriteResult.NotPermitted => localizer.Get("clan.motd.notpermitted", culture),
        _ => localizer.Get("clan.motd.failed", culture),
    };
}
