using System.Globalization;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line vending-offer fragment used by <c>!vending</c>.</summary>
internal static class VendingLine
{
    /// <summary>Formats one offer as a single in-game chat fragment.</summary>
    /// <param name="offer">The offer to format.</param>
    /// <param name="names">Resolves the currency id to a display name.</param>
    /// <param name="localizer">The reply localizer.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>e.g. <c>D7 1 for 12 scrap (18 left)</c>.</returns>
    public static string Format(VendingOffer offer, IItemNameResolver names, ILocalizer localizer, string culture)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(localizer);

        var currencyName = names.Resolve(offer.Key.CurrencyId);
        var cost = string.Create(CultureInfo.InvariantCulture, $"{offer.CostPerOrder} {currencyName}");
        var stock = offer.InStock
            ? localizer.Get("command.vending.instock", culture, offer.AmountInStock)
            : localizer.Get("command.vending.soldout", culture);

        var line = localizer.Get("command.vending.offer", culture, offer.Grid, offer.Quantity, cost, stock);

        // !vending names the item once, above the rows, so this fragment has no item-name slot of its
        // own — the blueprint marker therefore goes on the whole fragment. Without it a rival's Metal
        // Pipe *blueprint* at 5 scrap sits next to the pipe itself at 40 and reads as a steal.
        return ListingDisplay.MarkBlueprint(line, offer.Key.ItemIsBlueprint, localizer, culture);
    }
}
