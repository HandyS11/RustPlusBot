using NSubstitute;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.Vending.Modules;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="VendingUntrackAutocompleteHandler"/>'s listing-label rendering.</summary>
public sealed class VendingUntrackAutocompleteHandlerTests
{
    private static readonly ListingKey Pipe = new(69511070, false, -932201673, false);

    private static ILocalizer StubLocalizer()
    {
        // Mirrors VendingEmbedRendererTests: a "French-ish" stub that proves the label is built entirely
        // from localized, formatted resource strings rather than any interpolated English literal — if
        // " x " or " for " ever leaked back in, it could only come from this handler's own code, since
        // the stub itself never produces those substrings.
        var localizer = Substitute.For<ILocalizer>();
        localizer.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => ci.ArgAt<string>(0));
        localizer.Get(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(ci => $"{ci.ArgAt<string>(0)}|{string.Join('|', ci.ArgAt<object[]>(2))}");
        return localizer;
    }

    private static IItemDatabase ItemDatabase()
    {
        var items = Substitute.For<IItemDatabase>();
        items.GetById(Pipe.ItemId)
            .Returns(new ItemRecord(Pipe.ItemId, "Tuyau en métal", 1, null, null, null, null, null, null));
        items.GetById(Pipe.CurrencyId)
            .Returns(new ItemRecord(Pipe.CurrencyId, "Ferraille", 1, null, null, null, null, null, null));
        return items;
    }

    [Fact]
    public void DisplayName_UsesTheSharedVtrackedFormat_NoHardcodedEnglish()
    {
        var listing = new TrackedListing(Pipe, 5, 12);
        var name = VendingUntrackAutocompleteHandler.DisplayName(listing, ItemDatabase(), StubLocalizer(), "fr");

        // Comes from command.vtracked.listing (quantity, item, cost, currency), not an interpolated
        // "{qty} x {item} for {cost} {currency}" — the literal words a French guild must never see.
        Assert.Equal("command.vtracked.listing|5|Tuyau en métal|12|Ferraille", name);
        Assert.DoesNotContain(" x ", name, StringComparison.Ordinal);
        Assert.DoesNotContain(" for ", name, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayName_BlueprintListing_MarksTheItemThroughTheSameLocalizer()
    {
        var blueprintKey = Pipe with
        {
            ItemIsBlueprint = true
        };
        var listing = new TrackedListing(blueprintKey, 5, 12);
        var name = VendingUntrackAutocompleteHandler.DisplayName(listing, ItemDatabase(), StubLocalizer(), "fr");

        Assert.Contains("vending.listing.blueprint|Tuyau en métal", name, StringComparison.Ordinal);
        Assert.DoesNotContain(" x ", name, StringComparison.Ordinal);
        Assert.DoesNotContain(" for ", name, StringComparison.Ordinal);
    }
}
