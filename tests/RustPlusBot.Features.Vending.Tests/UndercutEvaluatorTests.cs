using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Vending.Evaluating;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="UndercutEvaluator"/>.</summary>
public sealed class UndercutEvaluatorTests
{
    private const uint WorldSize = 4000;
    private const int Pipe = 69511070;
    private const int Scrap = -932201673;
    private const int Cloth = -858312878;

    /// <summary>Our position, far enough from <see cref="RivalX"/> to land in a different grid cell (one cell is 146.25 units).</summary>
    private const float MineX = 500f, MineY = 3000f;

    /// <summary>The rival's position; see <see cref="MineX"/>.</summary>
    private const float RivalX = 2000f, RivalY = 1000f;

    private static readonly HashSet<string> MyGrid = new(StringComparer.OrdinalIgnoreCase)
    {
        MapGrid.LabelFor(MineX, MineY, WorldSize, MapGridStyle.InGame),
    };

    private static VendingMachineSnapshot Machine(
        ulong id,
        float x,
        float y,
        params VendingOfferSnapshot[] offers) =>
        new(id, x, y, $"Shop{id}", false, offers);

    private static VendingOfferSnapshot Sell(
        int qty,
        int cost,
        int stock,
        int currency = Scrap,
        int item = Pipe) =>
        new(item, false, qty, currency, false, cost, stock);

    private static IReadOnlyList<UndercutNotice> Evaluate(params VendingMachineSnapshot[] machines) =>
        UndercutEvaluator.Evaluate(machines, WorldSize, MapGridStyle.InGame, MyGrid, []);

    [Fact]
    public void CheaperRival_RaisesNotice()
    {
        var notice = Assert.Single(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 8, 5))));

        Assert.Equal(Pipe, notice.Key.ItemId);
        Assert.Equal(10, notice.ReferenceCostPerOrder);
        Assert.Equal(2UL, Assert.Single(notice.Undercutters).MachineId);
    }

    [Fact]
    public void EqualPriceRival_RaisesNotice()
    {
        var notice = Assert.Single(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 10, 5))));

        Assert.Single(notice.Undercutters);
    }

    [Fact]
    public void DearerRival_RaisesNothing()
    {
        Assert.Empty(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 12, 5))));
    }

    [Fact]
    public void ReferenceIsMyLowestPrice_SoAMidPricedRivalIsNotAThreat()
    {
        // I sell at 5 and at 6; a rival at 6 has not beaten my best price.
        Assert.Empty(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 5, 5)),
            Machine(3UL, MineX, MineY, Sell(1, 6, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 6, 5))));
    }

    [Fact]
    public void SoldOutRival_RaisesNothing()
    {
        Assert.Empty(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 1, 0))));
    }

    [Fact]
    public void DifferentCurrency_IsNeverCompared()
    {
        Assert.Empty(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 1, 5, currency: Cloth))));
    }

    [Fact]
    public void BlueprintAndItem_AreDistinctListings()
    {
        var mine = Machine(1UL, MineX, MineY, new VendingOfferSnapshot(Pipe, true, 1, Scrap, false, 10, 5));
        var rival = Machine(2UL, RivalX, RivalY, new VendingOfferSnapshot(Pipe, false, 1, Scrap, false, 1, 5));

        Assert.Empty(Evaluate(mine, rival));
    }

    [Fact]
    public void BundledRival_UndercutsOnUnitPrice()
    {
        // Rival sells 2 for 10 (5 each) against my 1 for 6.
        var notice = Assert.Single(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 6, 5)),
            Machine(2UL, RivalX, RivalY, Sell(2, 10, 5))));

        Assert.Single(notice.Undercutters);
    }

    [Fact]
    public void MachineInMyGrid_IsNeverAnUndercutter()
    {
        Assert.Empty(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(3UL, MineX, MineY, Sell(1, 1, 5))));
    }

    [Fact]
    public void ManualListing_IsTrackedWithoutAMachine()
    {
        var notices = UndercutEvaluator.Evaluate(
            [Machine(2UL, RivalX, RivalY, Sell(1, 4, 5))],
            WorldSize,
            MapGridStyle.InGame,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [new TrackedListing(new ListingKey(Pipe, false, Scrap, false), 1, 5)]);

        Assert.Single(Assert.Single(notices).Undercutters);
    }

    [Fact]
    public void Undercutters_AreOrderedCheapestFirst()
    {
        var notice = Assert.Single(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 9, 5)),
            Machine(4UL, RivalX + 500f, RivalY, Sell(1, 4, 5))));

        Assert.Equal(4UL, notice.Undercutters[0].MachineId);
    }
}
