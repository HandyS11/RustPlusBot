using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Vending.Searching;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="VendingSearch"/>.</summary>
public sealed class VendingSearchTests
{
    private static readonly ListingKey Pipe = new(69511070, false, -932201673, false);

    private static VendingOffer Offer(string grid, int qty, int cost, int stock) =>
        new(1UL, "Shop", grid, Pipe, qty, cost, stock);

    [Fact]
    public void Order_PutsInStockBeforeSoldOut_EvenWhenDearer()
    {
        var ordered = VendingSearch.Order([Offer("A1", 1, 20, 5), Offer("B2", 1, 5, 0)]);

        Assert.Equal("A1", ordered[0].Grid);
        Assert.Equal("B2", ordered[1].Grid);
    }

    [Fact]
    public void Order_SortsByUnitPriceNotOrderCost()
    {
        // 20 for 100 is 5 each and must beat 1 for 6, despite costing far more per order.
        var ordered = VendingSearch.Order([Offer("A1", 1, 6, 5), Offer("B2", 20, 100, 5)]);

        Assert.Equal("B2", ordered[0].Grid);
    }

    [Fact]
    public void Order_TiesBreakByGrid_SoResultsAreStable()
    {
        var ordered = VendingSearch.Order([Offer("K12", 1, 5, 5), Offer("B3", 1, 5, 5)]);

        Assert.Equal("B3", ordered[0].Grid);
    }

    [Fact]
    public void Take_ReportsHowManyWereNotShown()
    {
        var offers = VendingSearch.Order([
            Offer("A1", 1, 5, 5), Offer("B2", 1, 6, 5), Offer("C3", 1, 7, 5), Offer("D4", 1, 8, 5)
        ]);

        var (shown, more) = VendingSearch.Take(offers, 3);

        Assert.Equal(3, shown.Count);
        Assert.Equal(1, more);
    }

    [Fact]
    public void Take_UnderLimit_ReportsNoneHidden()
    {
        var (shown, more) = VendingSearch.Take([Offer("A1", 1, 5, 5)], 10);

        Assert.Single(shown);
        Assert.Equal(0, more);
    }

    [Fact]
    public void UnitPriceComparer_IsATotalOrder_NullNeverComparesEqualToANonNullOffer()
    {
        // IComparer<T> requires a total order. Compare(null, x) == 0 for a non-null x would violate that
        // (x would then have to compare equal to null too, by symmetry) and risks surprising sort
        // behaviour if a null ever reaches OrderBy/ThenBy. Nulls must sort consistently to one end.
        var comparer = VendingSearch.UnitPriceComparer.Instance;
        var offer = Offer("A1", 1, 5, 5);

        Assert.NotEqual(0, comparer.Compare(null, offer));
        Assert.NotEqual(0, comparer.Compare(offer, null));
        Assert.Equal(0, comparer.Compare(null, null));

        // And the two non-equal comparisons must actually be opposite in sign, not both -1 or both 1 —
        // that is what "sort consistently to one end" means.
        Assert.Equal(-comparer.Compare(null, offer), comparer.Compare(offer, null));
    }
}
