using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Domain.Vending;
using RustPlusBot.Features.Vending.Indexing;
using RustPlusBot.Features.Vending.Tracking;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Vending;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="VendingTrackService"/>.</summary>
public sealed class VendingTrackServiceTests
{
    private const ulong GuildId = 10UL;
    private const uint WorldSize = 4000;
    private const int PipeId = 69511070;
    private const int ClothId = -858312878;
    private const int Scrap = -932201673;

    /// <summary>A coordinate inside D7 on a 4000 world with the in-game grid convention.</summary>
    private const float InD7X = 500f, InD7Y = 2900f;

    /// <summary>A second, distinct coordinate that still bins to D7.</summary>
    private const float AlsoInD7X = 450f, AlsoInD7Y = 2850f;

    /// <summary>A coordinate well away from D7.</summary>
    private const float ElsewhereX = 2000f, ElsewhereY = 1000f;

    private static readonly Guid ServerId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly ListingKey Pipe = new(PipeId, false, Scrap, false);

    private readonly IVendingStore _store = Substitute.For<IVendingStore>();

    private readonly VendingIndex _index = new();

    private readonly VendingTrackService _service;

    /// <summary>Builds the service over a substituted store and a real, initially empty index.</summary>
    public VendingTrackServiceTests()
    {
        var mapSettings = Substitute.For<IMapSettingsStore>();
        mapSettings.GetAsync(default, Guid.Empty, default).ReturnsForAnyArgs(MapLayerSettings.AllOn);
        _service = new VendingTrackService(_store, _index, mapSettings);
    }

    private static VendingOfferSnapshot Sell(int itemId) => new(itemId, false, 1, Scrap, false, 10, 5);

    private static VendingMachineSnapshot Machine(ulong id, float x, float y, params VendingOfferSnapshot[] offers) =>
        new(id, x, y, "Shop", false, offers);

    [Fact]
    public async Task TrackGrid_NullGrid_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.TrackGridAsync(GuildId, ServerId, null!, 1UL, CancellationToken.None));

    [Fact]
    public async Task TrackGrid_ServerNeverPolled_ReportsInvalidAndRegistersNothing()
    {
        // Nothing has been indexed for this server, so there is no map to validate the cell against.
        // Registering anyway would accept any string at all as a grid reference.
        var result = await _service.TrackGridAsync(GuildId, ServerId, "D7", 1UL, CancellationToken.None);

        Assert.False(result.GridValid);
        await _store.DidNotReceiveWithAnyArgs().AddGridAsync(default, Guid.Empty, default!, default, default);
    }

    [Fact]
    public async Task TrackGrid_WorldSizeUnknown_ReportsInvalidAndRegistersNothing()
    {
        // A poll can land before the map dimensions do, leaving WorldSize 0. MapGrid.CellCount clamps
        // that to a single cell, so every machine on the server would answer to "A0" — registering
        // against it would silently claim the whole map.
        _index.Replace(GuildId, ServerId, 0, [Machine(1UL, InD7X, InD7Y, Sell(PipeId))]);

        var result = await _service.TrackGridAsync(GuildId, ServerId, "A0", 1UL, CancellationToken.None);

        Assert.False(result.GridValid);
        await _store.DidNotReceiveWithAnyArgs().AddGridAsync(default, Guid.Empty, default!, default, default);
    }

    [Fact]
    public async Task TrackGrid_CellThatDoesNotExistOnThisMap_ReportsInvalidAndRegistersNothing()
    {
        // "Z99" is well-formed but a 4000 world only has rows 0-27, so nobody could ever reach it.
        _index.Replace(GuildId, ServerId, WorldSize, [Machine(1UL, InD7X, InD7Y, Sell(PipeId))]);

        var result = await _service.TrackGridAsync(GuildId, ServerId, "Z99", 1UL, CancellationToken.None);

        Assert.False(result.GridValid);
        Assert.Equal(0, result.MachinesFound);
        await _store.DidNotReceiveWithAnyArgs().AddGridAsync(default, Guid.Empty, default!, default, default);
    }

    [Fact]
    public async Task TrackGrid_ValidCell_RegistersTheNormalisedCellAndCountsOnlyWhatStandsInIt()
    {
        _index.Replace(GuildId, ServerId, WorldSize,
        [
            Machine(1UL, InD7X, InD7Y, Sell(PipeId), Sell(ClothId)),
            Machine(2UL, AlsoInD7X, AlsoInD7Y, Sell(PipeId)),
            Machine(3UL, ElsewhereX, ElsewhereY, Sell(PipeId)),
        ]);

        var result = await _service.TrackGridAsync(GuildId, ServerId, " d7 ", 4242UL, CancellationToken.None);

        // Registered under the canonical label: the store compares grids as stored strings, so " d7 "
        // and "D7" reaching it unnormalised would register the same cell twice and never match again.
        await _store.Received(1).AddGridAsync(GuildId, ServerId, "D7", 4242UL, Arg.Any<CancellationToken>());
        Assert.True(result.GridValid);

        // Two machines stand in D7; the third is a different cell and must not be counted.
        Assert.Equal(2, result.MachinesFound);

        // Three sell orders across those two machines, but pipes-for-scrap is offered by both: the
        // count is of distinct listings, which is what the reply promises the player.
        Assert.Equal(2, result.ListingsTracked);
    }

    [Fact]
    public async Task UntrackGrid_NullGrid_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.UntrackGridAsync(GuildId, ServerId, null!, CancellationToken.None));

    [Fact]
    public async Task UntrackGrid_NormalisesBeforeAskingTheStore()
    {
        // Grids are stored upper-cased, so an unnormalised " d7 " would match no row and the player
        // would be told their cell was never tracked.
        _store.RemoveGridAsync(GuildId, ServerId, "D7", Arg.Any<CancellationToken>()).Returns(true);

        var removed = await _service.UntrackGridAsync(GuildId, ServerId, " d7 ", CancellationToken.None);

        Assert.True(removed);
        await _store.Received(1).RemoveGridAsync(GuildId, ServerId, "D7", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UntrackGrid_UnknownCell_ReportsTheStoresAnswer()
    {
        _store.RemoveGridAsync(default, Guid.Empty, default!, default).ReturnsForAnyArgs(false);

        Assert.False(await _service.UntrackGridAsync(GuildId, ServerId, "D7", CancellationToken.None));
    }

    [Fact]
    public async Task TrackListing_PersistsTheListingAgainstTheRegisteringUser()
    {
        await _service.TrackListingAsync(GuildId, ServerId, Pipe, 2, 40, 77UL, CancellationToken.None);

        await _store.Received(1)
            .UpsertListingAsync(GuildId, ServerId, Pipe, 2, 40, 77UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UntrackListing_ReportsTheStoresAnswer()
    {
        _store.RemoveListingAsync(GuildId, ServerId, Pipe, Arg.Any<CancellationToken>()).Returns(true);

        Assert.True(await _service.UntrackListingAsync(GuildId, ServerId, Pipe, CancellationToken.None));
        await _store.Received(1).RemoveListingAsync(GuildId, ServerId, Pipe, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTracked_ProjectsThePersistedRowsOntoTheSummary()
    {
        _store.ListGridsAsync(GuildId, ServerId, Arg.Any<CancellationToken>()).Returns(["A1", "D7"]);
        _store.ListListingsAsync(GuildId, ServerId, Arg.Any<CancellationToken>()).Returns(
        [
            new VendingListingTrack
            {
                GuildId = GuildId,
                ServerId = ServerId,
                ItemId = PipeId,
                ItemIsBlueprint = true,
                CurrencyId = Scrap,
                CurrencyIsBlueprint = false,
                Quantity = 3,
                CostPerOrder = 45,
            },
        ]);

        var summary = await _service.GetTrackedAsync(GuildId, ServerId, CancellationToken.None);

        Assert.Equal(["A1", "D7"], summary.Grids);
        var listing = Assert.Single(summary.Listings);

        // The blueprint flag has to survive the projection: the same item id at the same price means
        // something entirely different when it is the blueprint rather than the item.
        Assert.Equal(new ListingKey(PipeId, true, Scrap, false), listing.Key);
        Assert.Equal(3, listing.Quantity);
        Assert.Equal(45, listing.CostPerOrder);
    }
}
