using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Connections.Servers;
using RustPlusBot.Features.ItemData;

namespace RustPlusBot.Features.Vending.Modules;

/// <summary>
/// Offers the guild's registered grid cells and manually tracked listings as choices for
/// <c>/vending-untrack</c>'s <c>target</c> option. Manual listings are shown with resolved item/currency
/// names — a player cannot pick a raw Rust item id like <c>69511070</c> out of a list — while the choice
/// value encodes enough to identify what to remove without a second lookup: <c>grid:{GRID}</c> for a
/// registered grid cell, or <c>listing:{itemId}:{itemIsBlueprint}:{currencyId}:{currencyIsBlueprint}</c>
/// for a manually tracked listing.
/// </summary>
public sealed class VendingUntrackAutocompleteHandler : AutocompleteHandler
{
    /// <inheritdoc />
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(autocompleteInteraction);
        ArgumentNullException.ThrowIfNull(services);

        if (context.Guild is null)
        {
            return AutocompletionResult.FromSuccess();
        }

        var typed = autocompleteInteraction.Data.Current.Value as string;

        // The "server" option may have already been filled in (it is declared after "target", so it is
        // only present once the user has gone back and picked one). Reuse it when present so a multi-server
        // guild gets the right list; otherwise fall through to ServerResolver's own single-server default.
        var serverArg = autocompleteInteraction.Data.Options
            .FirstOrDefault(o => o.Name == "server")?.Value as string;

        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var resolver = scope.ServiceProvider.GetRequiredService<ServerResolver>();

            // The resolution's error message is never shown here (autocomplete has no room for prose); the
            // culture only affects that unused text, so "en" is fine.
            var resolution = await resolver
                .ResolveAsync(context.Guild.Id, serverArg, "en", CancellationToken.None).ConfigureAwait(false);
            if (resolution.ServerId is not { } serverId)
            {
                return AutocompletionResult.FromSuccess();
            }

            var trackService = scope.ServiceProvider.GetRequiredService<IVendingTrackService>();
            var items = scope.ServiceProvider.GetRequiredService<IItemDatabase>();
            var summary = await trackService
                .GetTrackedAsync(context.Guild.Id, serverId, CancellationToken.None).ConfigureAwait(false);

            var choices = summary.Grids
                .Select(grid => new AutocompleteResult(grid, $"grid:{grid}"))
                .Concat(summary.Listings.Select(listing => new AutocompleteResult(
                    DisplayName(listing, items),
                    $"listing:{listing.Key.ItemId}:{listing.Key.ItemIsBlueprint}:" +
                    $"{listing.Key.CurrencyId}:{listing.Key.CurrencyIsBlueprint}")))
                .Where(c => string.IsNullOrEmpty(typed) || c.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
                .Take(25);

            return AutocompletionResult.FromSuccess(choices);
        }
    }

    private static string DisplayName(TrackedListing listing, IItemDatabase items) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{listing.Quantity} x {ItemName(items, listing.Key.ItemId)} for " +
            $"{listing.CostPerOrder} {ItemName(items, listing.Key.CurrencyId)}");

    private static string ItemName(IItemDatabase items, int itemId) =>
        items.GetById(itemId)?.Name ?? itemId.ToString(CultureInfo.InvariantCulture);
}
