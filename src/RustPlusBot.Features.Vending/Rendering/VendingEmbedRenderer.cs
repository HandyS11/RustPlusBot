using System.Globalization;
using Discord;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.Vending.Evaluating;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Vending.Rendering;

/// <summary>Renders vending notices and search results as Discord embeds. Pure — same input, same output.</summary>
/// <param name="items">Resolves item/currency ids to display names.</param>
/// <param name="localizer">The shared localizer.</param>
internal sealed class VendingEmbedRenderer(IItemDatabase items, ILocalizer localizer)
{
    /// <summary>Renders the "your listing is being undercut" notice.</summary>
    /// <param name="notice">Our listing plus every rival offer at or below our price, cheapest first.</param>
    /// <param name="culture">The guild culture (BCP-47 primary tag).</param>
    /// <returns>The undercut embed.</returns>
    public Embed RenderUndercut(UndercutNotice notice, string culture)
    {
        ArgumentNullException.ThrowIfNull(notice);

        var title = localizer.Get("vending.undercut.title", culture, ItemName(notice.Key.ItemId));
        var yours = localizer.Get("vending.undercut.yours", culture,
            FormatCostCurrency(notice.ReferenceCostPerOrder, notice.Key.CurrencyId),
            FormatQuantityItem(notice.ReferenceQuantity, notice.Key.ItemId));

        string[] lines =
        [
            yours,
            ..notice.Undercutters.Select(rival => localizer.Get("vending.undercut.rival", culture,
                rival.Grid,
                FormatQuantityItem(rival.Quantity, rival.Key.ItemId),
                FormatCostCurrency(rival.CostPerOrder, rival.Key.CurrencyId),
                rival.AmountInStock)),
        ];

        return new EmbedBuilder().WithTitle(title).WithDescription(string.Join('\n', lines)).Build();
    }

    /// <summary>Renders the "one of your machines sold out" notice.</summary>
    /// <param name="notice">The machine's dead listings, or a wholly empty shop.</param>
    /// <param name="culture">The guild culture (BCP-47 primary tag).</param>
    /// <returns>The sell-out embed.</returns>
    public Embed RenderStock(StockNotice notice, string culture)
    {
        ArgumentNullException.ThrowIfNull(notice);

        var title = localizer.Get("vending.stock.title", culture, notice.ShopName ?? notice.Grid);

        // A wholly empty machine reports no individual listings (see StockNotice.SoldOut), so the empty
        // case is handled without ever touching notice.SoldOut.
        var description = notice.MachineEmpty
            ? localizer.Get("vending.stock.empty", culture)
            : string.Join('\n', notice.SoldOut.Select(offer =>
                localizer.Get("vending.stock.item", culture, FormatQuantityItem(offer.Quantity, offer.Key.ItemId))));

        return new EmbedBuilder().WithTitle(title).WithDescription(description).Build();
    }

    /// <summary>Renders a search result table for an item query.</summary>
    /// <param name="itemName">The resolved item name being searched for.</param>
    /// <param name="shown">The offers to display, in display order.</param>
    /// <param name="more">The count of additional matching offers hidden by the display limit.</param>
    /// <param name="culture">The guild culture (BCP-47 primary tag).</param>
    /// <returns>The search result embed.</returns>
    public Embed RenderSearch(string itemName, IReadOnlyList<VendingOffer> shown, int more, string culture)
    {
        ArgumentNullException.ThrowIfNull(itemName);
        ArgumentNullException.ThrowIfNull(shown);

        var title = localizer.Get("vending.search.title", culture, itemName);
        var description = shown.Count == 0
            ? localizer.Get("vending.search.none", culture, itemName)
            : string.Join('\n', shown.Select(offer => localizer.Get("vending.search.row", culture,
                offer.Grid,
                FormatQuantityItem(offer.Quantity, offer.Key.ItemId),
                FormatCostCurrency(offer.CostPerOrder, offer.Key.CurrencyId),
                offer.InStock
                    ? localizer.Get("vending.search.instock", culture, offer.AmountInStock)
                    : localizer.Get("vending.search.soldout", culture))));

        var builder = new EmbedBuilder().WithTitle(title).WithDescription(description);
        if (more > 0)
        {
            builder.WithFooter(localizer.Get("vending.search.more", culture, more));
        }

        return builder.Build();
    }

    private string ItemName(int itemId) => items.GetById(itemId)?.Name ?? itemId.ToString(CultureInfo.InvariantCulture);

    private string FormatQuantityItem(int quantity, int itemId) =>
        string.Create(CultureInfo.InvariantCulture, $"{quantity} x {ItemName(itemId)}");

    private string FormatCostCurrency(int cost, int currencyId) =>
        string.Create(CultureInfo.InvariantCulture, $"{cost} {ItemName(currencyId)}");
}
