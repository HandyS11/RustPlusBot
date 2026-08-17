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

        return localizer.Get("command.vending.offer", culture, offer.Grid, offer.Quantity, cost, stock);
    }
}
