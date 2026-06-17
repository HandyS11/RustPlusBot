using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Commands.Servers;

/// <summary>Offers the guild's registered servers (by name) as choices for the slash <c>server</c> option.</summary>
public sealed class ServerAutocompleteHandler : AutocompleteHandler
{
    /// <inheritdoc />
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(services);

        if (context.Guild is null)
        {
            return AutocompletionResult.FromSuccess();
        }

        var typed = autocompleteInteraction.Data.Current.Value as string;
        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var known = await servers.ListAsync(context.Guild.Id).ConfigureAwait(false);
            var results = known
                .Where(s => string.IsNullOrEmpty(typed) ||
                            s.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
                .Take(25)
                .Select(s => new AutocompleteResult(s.Name, s.Id.ToString()));
            return AutocompletionResult.FromSuccess(results);
        }
    }
}
