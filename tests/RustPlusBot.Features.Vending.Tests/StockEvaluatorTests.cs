using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Vending.Evaluating;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="StockEvaluator"/>.</summary>
public sealed class StockEvaluatorTests
{
    private const uint WorldSize = 4000;
    private const int Scrap = -932201673;
    private const float MineX = 500f, MineY = 3000f;
    private const float RivalX = 2000f, RivalY = 1000f;

    private static readonly HashSet<string> MyGrid = new(StringComparer.OrdinalIgnoreCase)
    {
        MapGrid.LabelFor(MineX, MineY, WorldSize, MapGridStyle.InGame),
    };

    private static VendingOfferSnapshot Sell(int item, int stock) =>
        new(item, false, 1, Scrap, false, 10, stock);

    private static IReadOnlyList<StockNotice> Evaluate(params VendingMachineSnapshot[] machines) =>
        StockEvaluator.Evaluate(machines, WorldSize, MapGridStyle.InGame, MyGrid);

    [Fact]
    public void SoldOutOffer_IsListed()
    {
        var notice = Assert.Single(Evaluate(
            new VendingMachineSnapshot(1UL, MineX, MineY, "Shop", false, [Sell(101, 0), Sell(102, 4)])));

        Assert.False(notice.MachineEmpty);
        Assert.Equal(101, Assert.Single(notice.SoldOut).Key.ItemId);
    }

    [Fact]
    public void EmptyMachineFlag_CollapsesInsteadOfEnumerating()
    {
        var notice = Assert.Single(Evaluate(
            new VendingMachineSnapshot(1UL, MineX, MineY, "Shop", true, [Sell(101, 0), Sell(102, 0)])));

        Assert.True(notice.MachineEmpty);
        Assert.Empty(notice.SoldOut);
        Assert.Equal("*", notice.Signature);
    }

    [Fact]
    public void UnknownFlag_FallsBackToPerOfferStock()
    {
        var notice = Assert.Single(Evaluate(
            new VendingMachineSnapshot(1UL, MineX, MineY, "Shop", null, [Sell(101, 0)])));

        Assert.False(notice.MachineEmpty);
        Assert.Single(notice.SoldOut);
    }

    [Fact]
    public void FullyStockedMachine_RaisesNothing()
    {
        Assert.Empty(Evaluate(
            new VendingMachineSnapshot(1UL, MineX, MineY, "Shop", false, [Sell(101, 4)])));
    }

    [Fact]
    public void MachineOutsideMyGrids_IsIgnored()
    {
        Assert.Empty(Evaluate(
            new VendingMachineSnapshot(9UL, RivalX, RivalY, "Rival", true, [Sell(101, 0)])));
    }

    [Fact]
    public void Signature_IsOrderIndependent()
    {
        var a = Assert.Single(Evaluate(
            new VendingMachineSnapshot(1UL, MineX, MineY, "Shop", false, [Sell(102, 0), Sell(101, 0)])));
        var b = Assert.Single(Evaluate(
            new VendingMachineSnapshot(1UL, MineX, MineY, "Shop", false, [Sell(101, 0), Sell(102, 0)])));

        Assert.Equal(a.Signature, b.Signature);
    }
}
