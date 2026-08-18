using System.Globalization;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Commands.Tests;

public sealed class VendingCommandHandlerTests
{
    private const int StubItemId = 317398316;
    private const int StubCurrencyId = -932201673;

    private static readonly ItemRecord StubItem = new(StubItemId, "Pipe", 1, null, null, null, null, null, null);

    /// <summary>Test double: returns the key unchanged (formatted args appended), so assertions can
    /// key off exact localization keys without depending on real resource strings.</summary>
    private sealed class StubLocalizer : ILocalizer
    {
        public string Get(string key, string culture) => key;

        public string Get(string key, string culture, params object[] args) =>
            args.Length == 0 ? key : $"{key}({string.Join(",", args)})";
    }

    private static CommandContext Context(IReadOnlyList<string> args) => new(1, Guid.NewGuid(), "en", 99, "Caller", args);

    private static VendingOffer Offer(string grid, int amountInStock) =>
        new(1, null, grid, new ListingKey(StubItemId, false, StubCurrencyId, false), 1, 100, amountInStock);

    private static VendingCommandHandler Create(IReadOnlyList<VendingOffer>? offers = null, bool hasData = true)
    {
        var readModel = Substitute.For<IVendingReadModel>();
        readModel.HasData(Arg.Any<ulong>(), Arg.Any<Guid>()).Returns(hasData);
        readModel.Search(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<MapGridStyle>())
            .Returns(offers ?? []);

        var items = Substitute.For<IItemDatabase>();
        items.Resolve(Arg.Any<string>()).Returns(new ItemMatch.Found(StubItem));

        var names = Substitute.For<IItemNameResolver>();
        names.Resolve(Arg.Any<int>()).Returns("Scrap");

        var mapSettings = Substitute.For<IMapSettingsStore>();
        mapSettings.GetAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(MapLayerSettings.AllOn);

        return new VendingCommandHandler(readModel, items, names, mapSettings, new StubLocalizer());
    }

    [Fact]
    public async Task Vending_NoArgs_ReturnsUsage()
    {
        var handler = Create();
        var reply = await handler.ExecuteAsync(Context([]), CancellationToken.None);

        Assert.Equal("command.vending.usage", reply);
    }

    [Fact]
    public async Task Vending_ShowsAtMostThreeOffers()
    {
        var handler = Create(offers: [Offer("A1", 5), Offer("B2", 6), Offer("C3", 7), Offer("D4", 8)]);
        var reply = await handler.ExecuteAsync(Context(["pipe"]), CancellationToken.None);

        Assert.DoesNotContain("D4", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vending_NotConnected_SaysSo()
    {
        var handler = Create(hasData: false);
        var reply = await handler.ExecuteAsync(Context(["pipe"]), CancellationToken.None);

        Assert.Equal("command.notconnected", reply);
    }

    [Fact]
    public async Task Vtracked_RendersManualListings_WithResolvedNames_NotRawIds()
    {
        const int listingItemId = 123456;
        const int listingCurrencyId = 654321;

        var trackService = Substitute.For<IVendingTrackService>();
        trackService.GetTrackedAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new VendingTrackSummary(
                [], [new TrackedListing(new ListingKey(listingItemId, false, listingCurrencyId, false), 5, 20)]));

        var names = Substitute.For<IItemNameResolver>();
        names.Resolve(listingItemId).Returns("Wood");
        names.Resolve(listingCurrencyId).Returns("Scrap");

        var handler = new VTrackedCommandHandler(trackService, names, new StubLocalizer());
        var reply = await handler.ExecuteAsync(Context([]), CancellationToken.None);

        Assert.Contains("Wood", reply, StringComparison.Ordinal);
        Assert.Contains("Scrap", reply, StringComparison.Ordinal);
        Assert.DoesNotContain(listingItemId.ToString(CultureInfo.InvariantCulture), reply, StringComparison.Ordinal);
        Assert.DoesNotContain(
            listingCurrencyId.ToString(CultureInfo.InvariantCulture), reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vtracked_BlueprintListing_IsMarkedAndFullyLocalized()
    {
        const int listingItemId = 123456;
        const int listingCurrencyId = 654321;

        var trackService = Substitute.For<IVendingTrackService>();
        trackService.GetTrackedAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new VendingTrackSummary(
                [], [new TrackedListing(new ListingKey(listingItemId, true, listingCurrencyId, false), 5, 20)]));

        var names = Substitute.For<IItemNameResolver>();
        names.Resolve(listingItemId).Returns("Wood");
        names.Resolve(listingCurrencyId).Returns("Scrap");

        var handler = new VTrackedCommandHandler(trackService, names, new StubLocalizer());
        var reply = await handler.ExecuteAsync(Context([]), CancellationToken.None);

        // The blueprint of an item and the item itself are different listings at wildly different
        // prices; the reply has to say which one this is.
        Assert.Contains("vending.listing.blueprint(Wood)", reply, StringComparison.Ordinal);

        // And the whole line comes from resources: a French guild must not be told "5 Bois for 20 Ferraille".
        Assert.Contains("command.vtracked.listing(", reply, StringComparison.Ordinal);
        Assert.DoesNotContain(" for ", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vending_BlueprintOffer_IsMarkedSoItIsNotMistakenForABargain()
    {
        // !vending names the item once above the rows, so the offer fragment carries the marker itself —
        // otherwise a blueprint at 100 scrap reads exactly like the item at 100 scrap.
        var blueprint = new VendingOffer(
            1, null, "A1", new ListingKey(StubItemId, true, StubCurrencyId, false), 1, 100, 5);
        var handler = Create(offers: [blueprint]);

        var reply = await handler.ExecuteAsync(Context(["pipe"]), CancellationToken.None);

        Assert.Contains("vending.listing.blueprint(", reply, StringComparison.Ordinal);
    }
}
