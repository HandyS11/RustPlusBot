using RustPlusBot.Abstractions.Vending;

namespace RustPlusBot.Features.Vending.Searching;

/// <summary>Orders and truncates search results. Pure.</summary>
internal static class VendingSearch
{
    /// <summary>Orders offers in-stock first, then cheapest per item, then by grid for stability.</summary>
    /// <param name="offers">The matching offers.</param>
    /// <returns>The offers in display order.</returns>
    public static IReadOnlyList<VendingOffer> Order(IEnumerable<VendingOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        return
        [
            .. offers
                .OrderByDescending(o => o.InStock)
                .ThenBy(o => o, UnitPriceComparer.Instance)
                .ThenBy(o => o.Grid, StringComparer.Ordinal)
        ];
    }

    /// <summary>Truncates to a limit, reporting how many were hidden.</summary>
    /// <param name="offers">The ordered offers.</param>
    /// <param name="limit">The maximum to show.</param>
    /// <returns>The shown offers and the count omitted.</returns>
    public static (IReadOnlyList<VendingOffer> Shown, int More) Take(IReadOnlyList<VendingOffer> offers, int limit)
    {
        ArgumentNullException.ThrowIfNull(offers);
        return offers.Count <= limit
            ? (offers, 0)
            : ([.. offers.Take(limit)], offers.Count - limit);
    }

    private sealed class UnitPriceComparer : IComparer<VendingOffer>
    {
        public static readonly UnitPriceComparer Instance = new();

        public int Compare(VendingOffer? x, VendingOffer? y) =>
            x is null || y is null
                ? 0
                : UnitPrice.Compare(x.CostPerOrder, x.Quantity, y.CostPerOrder, y.Quantity);
    }
}
