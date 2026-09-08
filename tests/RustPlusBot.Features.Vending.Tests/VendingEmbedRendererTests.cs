using System.Globalization;
using Discord;
using NSubstitute;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.Vending.Evaluating;
using RustPlusBot.Features.Vending.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="VendingEmbedRenderer"/>.</summary>
public sealed class VendingEmbedRendererTests
{
    private static readonly ListingKey Pipe = new(69511070, false, -932201673, false);

    private static VendingEmbedRenderer Create()
    {
        var localizer = Substitute.For<ILocalizer>();
        localizer.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => ci.ArgAt<string>(0));
        localizer.Get(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(ci => $"{ci.ArgAt<string>(0)}|{string.Join('|', ci.ArgAt<object[]>(2))}");
        return new VendingEmbedRenderer(Substitute.For<IItemDatabase>(), localizer);
    }

    [Fact]
    public void RenderUndercut_DescriptionContainsRivalGridAndPrice()
    {
        var renderer = Create();
        var notice = new UndercutNotice(Pipe, 1, 10,
            [new VendingOffer(2UL, "Rival", "K12", Pipe, 1, 8, 4)]);

        // The relay only edits when the render changes, so an unstable render would burn Discord calls
        // every five seconds forever. What actually matters is that the rendered text tells the reader
        // where the undercut is coming from (the rival's grid) and at what price (their cost per order).
        var description = renderer.RenderUndercut(notice, "en").Description;

        Assert.Contains("K12", description, StringComparison.Ordinal);
        Assert.Contains("8", description, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderStock_EmptyMachine_DoesNotEnumerateItems()
    {
        var renderer = Create();
        var notice = new StockNotice(1UL, "Shop", "D7", MachineEmpty: true, []);

        Assert.Contains("vending.stock.empty", renderer.RenderStock(notice, "en").Description,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RenderUndercut_MoreRivalsThanTheLimit_StaysWithinDiscordsDescriptionLimit()
    {
        // A popular item on a busy server draws far more than a screenful of rivals, and Discord throws
        // ArgumentException on a description over 4096 characters. That throw escapes the relay's
        // undercut pass, which is awaited before the sell-out pass — so an uncapped description would
        // silently kill *both* kinds of vending notification for the server, every poll, for as long as
        // the market stayed hot.
        var renderer = Create();
        var notice = new UndercutNotice(Pipe, 1, 100, [.. Enumerable.Range(0, 200).Select(Rival)]);

        var description = renderer.RenderUndercut(notice, "en").Description;

        Assert.True(description.Length <= EmbedBuilder.MaxDescriptionLength,
            $"description was {description.Length} characters");
        Assert.Equal(12, description.Split('\n').Length); // our line + 10 rivals + the "+N more" trailer.
        Assert.Contains("vending.search.more|190", description, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderStock_MoreSoldOutLinesThanTheLimit_StaysWithinDiscordsDescriptionLimit()
    {
        var renderer = Create();
        var notice = new StockNotice(1UL, "Shop", "D7", MachineEmpty: false,
            [.. Enumerable.Range(0, 200).Select(Rival)]);

        var description = renderer.RenderStock(notice, "en").Description;

        Assert.True(description.Length <= EmbedBuilder.MaxDescriptionLength,
            $"description was {description.Length} characters");
        Assert.Contains("vending.search.more|190", description, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderUndercut_BlueprintListing_IsDistinguishableFromThePlainItem()
    {
        // Same item id, wildly different value: without the marker a blueprint at 5 scrap renders
        // identically to the item at 40 and reads as a bargain.
        var renderer = Create();
        var blueprint = new ListingKey(Pipe.ItemId, true, Pipe.CurrencyId, false);
        var notice = new UndercutNotice(blueprint, 1, 10,
            [new VendingOffer(2UL, "Rival", "K12", blueprint, 1, 8, 4)]);

        var embed = renderer.RenderUndercut(notice, "en");

        Assert.Contains("vending.listing.blueprint", embed.Title, StringComparison.Ordinal);
        Assert.Contains("vending.listing.blueprint", embed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderStock_UnnamedShop_FallsBackToTheGridInTheTitle()
    {
        // A machine with no shopfront name would otherwise title as an empty string, leaving the owner
        // no way to tell which of their machines ran dry.
        var renderer = Create();
        var notice = new StockNotice(1UL, ShopName: null, "D7", MachineEmpty: true, []);

        Assert.Contains("D7", renderer.RenderStock(notice, "en").Title, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSearch_NullItemName_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Create().RenderSearch(null!, [], 0, "en"));

    [Fact]
    public void RenderSearch_NullOffers_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Create().RenderSearch("Pipe", null!, 0, "en"));

    [Fact]
    public void RenderSearch_NoMatches_SaysSoInsteadOfRenderingAnEmptyTable()
    {
        var renderer = Create();

        var embed = renderer.RenderSearch("Pipe", [], 0, "en");

        Assert.Contains("vending.search.none|Pipe", embed.Description, StringComparison.Ordinal);
        Assert.Null(embed.Footer);
    }

    [Fact]
    public void RenderSearch_SoldOutOfferIsMarkedDifferentlyFromAnInStockOne()
    {
        // A sold-out machine is still a real search hit — the price is informative — but sending a
        // player across the map to a shelf with nothing on it is the worst possible answer.
        var renderer = Create();

        var description = renderer.RenderSearch(
            "Pipe",
            [
                new VendingOffer(1UL, "Shop", "A1", Pipe, 1, 8, 4),
                new VendingOffer(2UL, "Shop", "B2", Pipe, 1, 6, 0),
            ],
            0,
            "en").Description;

        var rows = description.Split('\n');
        Assert.Equal(2, rows.Length);
        Assert.Contains("vending.search.instock|4", rows[0], StringComparison.Ordinal);
        Assert.Contains("A1", rows[0], StringComparison.Ordinal);
        Assert.Contains("vending.search.soldout", rows[1], StringComparison.Ordinal);
        Assert.Contains("B2", rows[1], StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSearch_HiddenMatches_AreReportedInTheFooter()
    {
        // The caller has already truncated, so the count it hands in is the only record that anything
        // was left out; dropping it would present a partial list as the whole market.
        var renderer = Create();

        var embed = renderer.RenderSearch("Pipe", [new VendingOffer(1UL, "Shop", "A1", Pipe, 1, 8, 4)], 7, "en");

        Assert.Equal("vending.search.more|7", embed.Footer?.Text);
        Assert.Single(embed.Description.Split('\n'));
    }

    [Fact]
    public void RenderSearch_ResolvesItemAndCurrencyNames_FallingBackToTheRawIdWhenUnknown()
    {
        // The dataset ships with the bot and the server does not, so a Rust update can introduce an id
        // the bot has never heard of. Printing the raw id is ugly but honest; printing nothing at all
        // would leave the row unreadable.
        var localizer = Substitute.For<ILocalizer>();
        localizer.Get(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(ci => $"{ci.ArgAt<string>(0)}|{string.Join('|', ci.ArgAt<object[]>(2))}");
        var items = Substitute.For<IItemDatabase>();
        items.GetById(Pipe.CurrencyId).Returns(new ItemRecord(
            Pipe.CurrencyId, "Scrap", 1000, null, null, null, null, null, null));
        var renderer = new VendingEmbedRenderer(items, localizer);

        var description = renderer
            .RenderSearch("Pipe", [new VendingOffer(1UL, "Shop", "A1", Pipe, 1, 8, 4)], 0, "en")
            .Description;

        Assert.Contains("8 Scrap", description, StringComparison.Ordinal);
        Assert.Contains(
            Pipe.ItemId.ToString(CultureInfo.InvariantCulture), description, StringComparison.Ordinal);
    }

    private static VendingOffer Rival(int index) =>
        new((ulong)index, "Rival", $"K{index}", Pipe, 1, 8, 4);
}
