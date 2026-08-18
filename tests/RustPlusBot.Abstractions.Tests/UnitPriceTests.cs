using RustPlusBot.Abstractions.Vending;

namespace RustPlusBot.Abstractions.Tests;

/// <summary>Unit tests for <see cref="UnitPrice"/>.</summary>
public sealed class UnitPriceTests
{
    [Fact]
    public void IsAtOrBelow_BundledCheaperOffer_Undercuts()
    {
        // 2 for 10 = 5 each, versus 1 for 6 = 6 each.
        Assert.True(UnitPrice.IsAtOrBelow(costA: 10, qtyA: 2, costB: 6, qtyB: 1));
    }

    [Fact]
    public void IsAtOrBelow_EqualUnitPrice_CountsAsUndercut()
    {
        // "less or the same resources" — matching the price is still a threat.
        Assert.True(UnitPrice.IsAtOrBelow(costA: 10, qtyA: 2, costB: 5, qtyB: 1));
    }

    [Fact]
    public void IsAtOrBelow_DearerBundle_DoesNotUndercut()
    {
        // 3 for 16 = 5.33 each, versus 1 for 5. Float rounding must not make this look equal.
        Assert.False(UnitPrice.IsAtOrBelow(costA: 16, qtyA: 3, costB: 5, qtyB: 1));
    }

    [Fact]
    public void Compare_OrdersByUnitPriceNotOrderCost()
    {
        // 20 for 100 (5 each) is cheaper than 1 for 6, despite the far larger order cost.
        Assert.True(UnitPrice.Compare(100, 20, 6, 1) < 0);
    }
}
