using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Modules;

/// <summary>Handles the language select menu in the #settings channel.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class SettingsComponentModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Persists the chosen culture and re-renders the workspace in the new language.</summary>
    /// <param name="selectedValues">The selected culture codes (expects exactly one).</param>
    [ComponentInteraction(SettingsMessageRenderer.LanguageSelectId)]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task SetCultureAsync(string[] selectedValues)
    {
        ArgumentNullException.ThrowIfNull(selectedValues);
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var culture = selectedValues.Length > 0 ? selectedValues[0] : "en";
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            await store.SetCultureAsync(Context.Guild.Id, culture).ConfigureAwait(false);

            var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
            await reconciler.ReconcileGlobalAsync(Context.Guild.Id).ConfigureAwait(false);

            await FollowupAsync($"Language set to `{culture}`.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
