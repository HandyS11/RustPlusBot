using System.Globalization;
using Discord;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.Vending.Evaluating;
using RustPlusBot.Features.Vending.Searching;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Vending.Rendering;

/// <summary>Renders vending notices and search results as Discord embeds. Pure — same input, same output.</summary>
/// <param name="items">Resolves item/currency ids to display names.</param>
/// <param name="localizer">The shared localizer.</param>
internal sealed class VendingEmbedRenderer(IItemDatabase items, ILocalizer localizer)
{
    /// <summary>
    /// The most offer rows any one embed description will list. Discord throws on a description over
    /// 4096 characters and nothing bounds how many rivals a hot item attracts, so the cap is what keeps
    /// a busy market from turning every render into an <see cref="ArgumentException"/> — which, thrown
    /// from the relay's first reconcile pass, would take the sell-out notices down with it. Ten matches
    /// what <c>/vending</c> shows; at ~50 characters a row that leaves an order of magnitude of slack.
    /// </summary>
    private const int MaxRows = 10;

    /// <summary>Renders the "your listing is being undercut" notice.</summary>
    /// <param name="notice">Our listing plus every rival offer at or below our price, cheapest first.</param>
    /// <param name="culture">The guild culture (BCP-47 primary tag).</param>
    /// <returns>The undercut embed.</returns>
    public Embed RenderUndercut(UndercutNotice notice, string culture)
    {
        ArgumentNullException.ThrowIfNull(notice);

        var title = localizer.Get("vending.undercut.title", culture,
            ItemDisplayName(notice.Key.ItemId, notice.Key.ItemIsBlueprint, culture));
        var yours = localizer.Get("vending.undercut.yours", culture,
            FormatCostCurrency(notice.ReferenceCostPerOrder, notice.Key.CurrencyId),
            FormatQuantityItem(notice.ReferenceQuantity, notice.Key, culture));

        var (shown, more) = VendingSearch.Take(notice.Undercutters, MaxRows);
        string[] lines =
        [
            yours,
            ..shown.Select(rival => localizer.Get("vending.undercut.rival", culture,
                rival.Grid,
                FormatQuantityItem(rival.Quantity, rival.Key, culture),
                FormatCostCurrency(rival.CostPerOrder, rival.Key.CurrencyId),
                rival.AmountInStock)),
            ..More(more, culture),
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
        string description;
        if (notice.MachineEmpty)
        {
            description = localizer.Get("vending.stock.empty", culture);
        }
        else
        {
            // A machine's sell orders are bounded in game, but nothing in this process enforces that, and
            // the cost of being wrong is the same thrown ArgumentException as above; cap it the same way.
            var (shown, more) = VendingSearch.Take(notice.SoldOut, MaxRows);
            string[] lines =
            [
                ..shown.Select(offer => localizer.Get("vending.stock.item", culture,
                    FormatQuantityItem(offer.Quantity, offer.Key, culture))),
                ..More(more, culture),
            ];
            description = string.Join('\n', lines);
        }

        return new EmbedBuilder().WithTitle(title).WithDescription(description).Build();
    }

    /// <summary>Renders a search result table for an item query.</summary>
    /// <param name="itemName">The resolved item name being searched for.</param>
    /// <param name="shown">The offers to display, in display order; already truncated by the caller.</param>
    /// <param name="more">The count of additional matching offers hidden by the display limit.</param>
    /// <param name="culture">The guild culture (BCP-47 primary tag).</param>
    /// <returns>The search result embed.</returns>
    public Embed RenderSearch(string itemName, IReadOnlyList<VendingOffer> shown, int more, string culture)
    {
        ArgumentNullException.ThrowIfNull(itemName);
        ArgumentNullException.ThrowIfNull(shown);

        var title = localizer.Get("vending.search.title", culture, itemName);

        // No cap is applied here: the caller has already run VendingSearch.Take and handed the omitted
        // count in as `more`, so truncating again would silently disagree with the footer.
        var description = shown.Count == 0
            ? localizer.Get("vending.search.none", culture, itemName)
            : string.Join('\n', shown.Select(offer => localizer.Get("vending.search.row", culture,
                offer.Grid,
                FormatQuantityItem(offer.Quantity, offer.Key, culture),
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

    /// <summary>The "+N more" trailer as zero or one line, so it can be spread into a row list.</summary>
    /// <param name="more">The number of rows the cap hid; zero means no trailer.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>An empty array, or the single localized trailer line.</returns>
    private string[] More(int more, string culture) =>
        more > 0 ? [localizer.Get("vending.search.more", culture, more)] : [];

    private string ItemName(int itemId) => items.GetById(itemId)?.Name ?? itemId.ToString(CultureInfo.InvariantCulture);

    /// <summary>An item's name, marked when the listing is for its blueprint rather than the item.</summary>
    /// <param name="itemId">The Rust item id.</param>
    /// <param name="isBlueprint">True when the listing is for the item's blueprint.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The display name.</returns>
    private string ItemDisplayName(int itemId, bool isBlueprint, string culture) =>
        ListingDisplay.MarkBlueprint(ItemName(itemId), isBlueprint, localizer, culture);

    private string FormatQuantityItem(int quantity, ListingKey key, string culture) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{quantity} x {ItemDisplayName(key.ItemId, key.ItemIsBlueprint, culture)}");

    private string FormatCostCurrency(int cost, int currencyId) =>
        string.Create(CultureInfo.InvariantCulture, $"{cost} {ItemName(currencyId)}");
}
