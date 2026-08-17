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
}
