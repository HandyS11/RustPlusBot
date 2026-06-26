using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class ItemLookupTests
{
    private static readonly ItemRecord Ak = new(1, "AK-47", 1, null, null, null, null);
    private static readonly ItemRecord Semi = new(2, "Semi-Automatic Rifle", 1, null, null, null, null);
    private static readonly ItemRecord Wood = new(3, "Wood", 1000, null, null, null, null);
    private static readonly ItemRecord Bag = new(4, "Sleeping Bag", 1, null, null, null, null);
    private static readonly IReadOnlyList<ItemRecord> All = [Ak, Semi, Wood, Bag];

    private static ItemMatch Resolve(string q) =>
        ItemLookup.Resolve(q, id => All.FirstOrDefault(i => i.Id == id), All);

    [Fact]
    public void NumericInput_ResolvesById()
    {
        var match = Resolve("3");
        var found = Assert.IsType<ItemMatch.Found>(match);
        Assert.Equal("Wood", found.Item.Name);
    }

    [Fact]
    public void NumericMiss_FallsThroughToNameMatch()
    {
        // "47" is not an id but is a substring of "AK-47".
        var match = Resolve("47");
        var found = Assert.IsType<ItemMatch.Found>(match);
        Assert.Equal("AK-47", found.Item.Name);
    }

    [Fact]
    public void ExactName_BeatsSubstring()
    {
        var match = Resolve("wood");
        var found = Assert.IsType<ItemMatch.Found>(match);
        Assert.Equal("Wood", found.Item.Name);
    }

    [Fact]
    public void SingleSubstring_Found()
    {
        var match = Resolve("sleep");
        var found = Assert.IsType<ItemMatch.Found>(match);
        Assert.Equal("Sleeping Bag", found.Item.Name);
    }

    [Fact]
    public void MultipleSubstring_AmbiguousPrefixFirst()
    {
        // "rifle" hits "Semi-Automatic Rifle"; "a" would be too broad. Use a query that hits 2.
        var match = ItemLookup.Resolve("a", id => All.FirstOrDefault(i => i.Id == id), All);
        var amb = Assert.IsType<ItemMatch.Ambiguous>(match);
        Assert.True(amb.Candidates.Count >= 2);
    }

    [Fact]
    public void NoMatch_NotFound()
    {
        Assert.IsType<ItemMatch.NotFound>(Resolve("zzzzz"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_NotFound(string q)
    {
        Assert.IsType<ItemMatch.NotFound>(Resolve(q));
    }

    [Fact]
    public void Ambiguous_RespectsCap()
    {
        var many = Enumerable.Range(1, 50).Select(i => new ItemRecord(i, $"Gun {i}", 1, null, null, null, null))
            .ToList();
        var match = ItemLookup.Resolve("gun", id => many.FirstOrDefault(x => x.Id == id), many, cap: 10);
        var amb = Assert.IsType<ItemMatch.Ambiguous>(match);
        Assert.Equal(10, amb.Candidates.Count);
    }
}
