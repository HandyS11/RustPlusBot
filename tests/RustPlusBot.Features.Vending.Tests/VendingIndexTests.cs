using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Vending.Indexing;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="VendingIndex"/>.</summary>
public sealed class VendingIndexTests
{
    private const int Pipe = 69511070;
    private const int Scrap = -932201673;
    private static readonly Guid ServerId = Guid.NewGuid();

    private static VendingMachineSnapshot Machine(ulong id, int cost, int stock) =>
        new(id, 500f, 3000f, "Shop", false, [new VendingOfferSnapshot(Pipe, false, 1, Scrap, false, cost, stock)]);

    [Fact]
    public void Replace_SupersedesThePreviousPoll()
    {
        var index = new VendingIndex();
        index.Replace(10UL, ServerId, 4000u, [Machine(1UL, 10, 5)]);
        index.Replace(10UL, ServerId, 4000u, [Machine(2UL, 7, 5)]);

        var offer = Assert.Single(index.Search(10UL, ServerId, Pipe, MapGridStyle.InGame));
        Assert.Equal(2UL, offer.MachineId);
    }

    [Fact]
    public void Search_UnknownServer_IsEmptyRatherThanThrowing()
    {
        var index = new VendingIndex();

        Assert.False(index.HasData(10UL, ServerId));
        Assert.Empty(index.Search(10UL, ServerId, Pipe, MapGridStyle.InGame));
    }

    [Fact]
    public void Clear_DropsTheServerState()
    {
        var index = new VendingIndex();
        index.Replace(10UL, ServerId, 4000u, [Machine(1UL, 10, 5)]);

        index.Clear(10UL, ServerId);

        Assert.False(index.HasData(10UL, ServerId));
    }

    [Fact]
    public void Search_ReturnsOnlyTheRequestedItem()
    {
        var index = new VendingIndex();
        index.Replace(10UL, ServerId, 4000u, [Machine(1UL, 10, 5)]);

        Assert.Empty(index.Search(10UL, ServerId, itemId: 999, MapGridStyle.InGame));
    }

    [Fact]
    public void Search_IncludesSoldOutOffers()
    {
        var index = new VendingIndex();
        index.Replace(10UL, ServerId, 4000u, [Machine(1UL, 10, stock: 0)]);

        var offer = Assert.Single(index.Search(10UL, ServerId, Pipe, MapGridStyle.InGame));
        Assert.False(offer.InStock);
    }
}
