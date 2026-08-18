using System.Globalization;
using Discord;
using NSubstitute;
using RustPlusBot.Features.Vending.Modules;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>
/// Unit tests for <see cref="VendingModule.Fit"/>, the guard that keeps /vending-tracked's embed fields
/// inside Discord's 1024-character limit.
/// </summary>
public sealed class VendingTrackedFieldTests
{
    private static ILocalizer Localizer()
    {
        var localizer = Substitute.For<ILocalizer>();
        localizer.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => ci.ArgAt<string>(0));
        localizer.Get(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(ci => $"{ci.ArgAt<string>(0)}|{string.Join('|', ci.ArgAt<object[]>(2))}");
        return localizer;
    }

    /// <summary>A listing line the length of a real one, e.g. "10 x Semi-Automatic Rifle — 250 Scrap".</summary>
    /// <param name="index">The listing's position, standing in for its quantity.</param>
    private static string Listing(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{index} x Semi-Automatic Rifle — 250 Scrap");

    [Fact]
    public void Fit_ShortList_IsLeftExactlyAsItWas()
    {
        var parts = new[]
        {
            "A1", "B2", "C3"
        };

        Assert.Equal("A1, B2, C3", VendingModule.Fit(parts, ", ", Localizer(), "en"));
    }

    [Fact]
    public void Fit_ThirtyListings_StaysWithinDiscordsFieldValueLimit()
    {
        // Registering thirty listings is the workflow /vending-track exists for, and thirty of these
        // lines is over 1100 characters — past that Discord rejects the field and /vending-tracked dies
        // with "The application did not respond" for as long as the team keeps them registered.
        string[] parts = [.. Enumerable.Range(1, 30).Select(Listing)];
        Assert.True(string.Join('\n', parts).Length > EmbedFieldBuilder.MaxFieldValueLength);

        var value = VendingModule.Fit(parts, "\n", Localizer(), "en");

        Assert.True(value.Length <= EmbedFieldBuilder.MaxFieldValueLength, $"value was {value.Length} characters");
        Assert.Contains("vending.tracked.omitted|", value, StringComparison.Ordinal);

        // Discord itself is the real oracle: build the field and let it validate.
        var embed = new EmbedBuilder().WithTitle("Tracked").AddField("Listings", value).Build();
        Assert.Equal(value, embed.Fields[0].Value);
    }

    [Fact]
    public void Fit_ManyGrids_TruncatesTheCommaSeparatedListToo()
    {
        // The Grids field joins on ", " rather than newlines, and a wipe-day team can register plenty of
        // cells; the same limit applies to it.
        string[] parts = [.. Enumerable.Range(1, 400).Select(i => $"AB{i}")];

        var value = VendingModule.Fit(parts, ", ", Localizer(), "en");

        Assert.True(value.Length <= EmbedFieldBuilder.MaxFieldValueLength, $"value was {value.Length} characters");
        Assert.Contains("vending.tracked.omitted|", value, StringComparison.Ordinal);
    }
}
