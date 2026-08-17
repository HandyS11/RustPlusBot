using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Vending.Ownership;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="GridOwnership"/>.</summary>
public sealed class GridOwnershipTests
{
    private const uint WorldSize = 4000;

    private static VendingMachineSnapshot Machine(float x, float y) =>
        new(1UL, x, y, "Shop", false, [new VendingOfferSnapshot(69511070, false, 2, -932201673, false, 10, 3)]);

    [Fact]
    public void GridOf_AgreesWithMapGrid()
    {
        var machine = Machine(500f, 3000f);
        Assert.Equal(
            MapGrid.LabelFor(500f, 3000f, WorldSize, MapGridStyle.InGame),
            GridOwnership.GridOf(machine, WorldSize, MapGridStyle.InGame));
    }

    [Fact]
    public void GridOf_DiffersBetweenGridStyles_AtTheInsetBoundary()
    {
        // The Rust+ style shifts rows 100 units south of the in-game style. At y = WorldSize - 200 the
        // in-game distance from the north edge is 200 units (row 1, since 146.25 < 200 < 292.5), while
        // the Rust+ distance is 200 - 100 = 100 units (row 0, since 100 < 146.25) — a genuine difference
        // that survives the row-clamping at the world edge. (WorldSize - 50 does not: both styles clamp
        // to row 0 there, because the Rust+ row would otherwise be negative.)
        var machine = Machine(500f, WorldSize - 200f);
        Assert.NotEqual(
            GridOwnership.GridOf(machine, WorldSize, MapGridStyle.InGame),
            GridOwnership.GridOf(machine, WorldSize, MapGridStyle.RustPlus));
    }

    [Fact]
    public void IsOwned_MatchesRegisteredCellCaseInsensitively()
    {
        var machine = Machine(500f, 3000f);
        var grid = GridOwnership.GridOf(machine, WorldSize, MapGridStyle.InGame);
        Assert.True(GridOwnership.IsOwned(machine, WorldSize, MapGridStyle.InGame,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { grid.ToLowerInvariant() }));
    }

    [Fact]
    public void IsOwned_UnregisteredCell_IsFalse()
    {
        var machine = Machine(500f, 3000f);
        Assert.False(GridOwnership.IsOwned(machine, WorldSize, MapGridStyle.InGame,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ZZ99" }));
    }

    [Fact]
    public void ToOffers_CarriesGridShopNameAndListingKey()
    {
        var machine = Machine(500f, 3000f);
        var offer = Assert.Single(GridOwnership.ToOffers(machine, WorldSize, MapGridStyle.InGame));

        Assert.Equal(1UL, offer.MachineId);
        Assert.Equal("Shop", offer.ShopName);
        Assert.Equal(GridOwnership.GridOf(machine, WorldSize, MapGridStyle.InGame), offer.Grid);
        Assert.Equal(new ListingKey(69511070, false, -932201673, false), offer.Key);
        Assert.Equal(2, offer.Quantity);
        Assert.Equal(10, offer.CostPerOrder);
        Assert.True(offer.InStock);
    }
}
