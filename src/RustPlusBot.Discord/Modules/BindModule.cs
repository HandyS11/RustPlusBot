using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Persistence.Bindings;

namespace RustPlusBot.Discord.Modules;

/// <summary>Binds a Discord channel to a bot feature.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per command.</param>
public sealed class BindModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Binds a channel to a bot feature.</summary>
    /// <param name="feature">Which feature this channel serves.</param>
    /// <param name="channel">Target text channel.</param>
    [SlashCommand("bind", "Bind a channel to a bot feature")]
    public async Task BindAsync(
        [Summary("feature", "Which feature this channel serves")]
        BoundFeature feature,
        [Summary("channel", "Target text channel")]
        ITextChannel channel)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<IBindingService>();
            await service.BindAsync(Context.Guild.Id, feature, channel.Id).ConfigureAwait(false);
            await RespondAsync($"Bound **{feature}** to <#{channel.Id}>.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
