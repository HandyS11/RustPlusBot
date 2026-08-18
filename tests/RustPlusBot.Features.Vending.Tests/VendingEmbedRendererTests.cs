using Discord;
using NSubstitute;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.ItemData;
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

        Assert.Contains("vending.stock.empty", renderer.RenderStock(notice, "en").Description, StringComparison.Ordinal);
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

    private static VendingOffer Rival(int index) =>
        new((ulong)index, "Rival", $"K{index}", Pipe, 1, 8, 4);
}
