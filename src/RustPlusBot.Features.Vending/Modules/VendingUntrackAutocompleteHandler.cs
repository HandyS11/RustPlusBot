using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Connections.Servers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;

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
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var loc = scope.ServiceProvider.GetRequiredService<ILocalizer>();
            var culture = await workspace.GetCultureAsync(context.Guild.Id).ConfigureAwait(false);
            var summary = await trackService
                .GetTrackedAsync(context.Guild.Id, serverId, CancellationToken.None).ConfigureAwait(false);

            var choices = summary.Grids
                .Select(grid => new AutocompleteResult(grid, $"grid:{grid}"))
                .Concat(summary.Listings.Select(listing => new AutocompleteResult(
                    DisplayName(listing, items, loc, culture),
                    $"listing:{listing.Key.ItemId}:{listing.Key.ItemIsBlueprint}:" +
                    $"{listing.Key.CurrencyId}:{listing.Key.CurrencyIsBlueprint}")))
                .Where(c => string.IsNullOrEmpty(typed) || c.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
                .Take(25);

            return AutocompletionResult.FromSuccess(choices);
        }
    }

    private static string DisplayName(TrackedListing listing, IItemDatabase items, ILocalizer loc, string culture) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{listing.Quantity} x {ItemDisplayName(items, loc, culture, listing.Key.ItemId, listing.Key.ItemIsBlueprint)} for " +
            $"{listing.CostPerOrder} {ItemName(items, listing.Key.CurrencyId)}");

    private static string ItemName(IItemDatabase items, int itemId) =>
        items.GetById(itemId)?.Name ?? itemId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// An item's display name, marked as a blueprint when that is what the listing sells — otherwise two
    /// listings for the same item id (one plain, one blueprint) show as identical suggestions and a
    /// player cannot tell which one they are picking. The rule lives in <see cref="ListingDisplay"/>;
    /// currency is never a blueprint by ruling, so only the item side goes through it.
    /// </summary>
    /// <param name="items">The item database, for name resolution.</param>
    /// <param name="loc">The localizer, for the blueprint indicator text.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="itemId">The Rust item id.</param>
    /// <param name="isBlueprint">True when this listing is for the item's blueprint.</param>
    private static string ItemDisplayName(
        IItemDatabase items, ILocalizer loc, string culture, int itemId, bool isBlueprint) =>
        ListingDisplay.MarkBlueprint(ItemName(items, itemId), isBlueprint, loc, culture);
}
