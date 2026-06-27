using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class ItemLookupTests
{
    private static readonly ItemRecord Ak = new(1, "AK-47", 1, null, null, null, null, null, null);
    private static readonly ItemRecord Semi = new(2, "Semi-Automatic Rifle", 1, null, null, null, null, null, null);
    private static readonly ItemRecord Wood = new(3, "Wood", 1000, null, null, null, null, null, null);
    private static readonly ItemRecord Bag = new(4, "Sleeping Bag", 1, null, null, null, null, null, null);
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
        // Query "a": "AK-47" starts with 'A' (prefix match); "Semi-Automatic Rifle" and
        // "Sleeping Bag" only contain 'a' as an interior character (substring matches).
        // Ranking: OrderByDescending(StartsWith) → "AK-47" must be at index 0.
        var match = ItemLookup.Resolve("a", id => All.FirstOrDefault(i => i.Id == id), All);
        var amb = Assert.IsType<ItemMatch.Ambiguous>(match);
        Assert.True(amb.Candidates.Count >= 2);
        Assert.Equal("AK-47", amb.Candidates[0].Name);
        Assert.DoesNotContain(amb.Candidates.Skip(1), c => c.Name.StartsWith('A') || c.Name.StartsWith('a'));
    }

    [Fact]
    public void ExactNameCollision_ReturnsAmbiguousOrderedById()
    {
        // Two items share the same case-insensitive name; the one with the lower id should be first.
        var recyclable = new ItemRecord(10, "Sunglasses", 1, null, new RecycleYield([]), null, null, null, null);
        var notRecyclable = new ItemRecord(5, "Sunglasses", 1, null, null, null, null, null, null);
        IReadOnlyList<ItemRecord> items = [recyclable, notRecyclable];
        var match = ItemLookup.Resolve("sunglasses", id => items.FirstOrDefault(i => i.Id == id), items);
        var amb = Assert.IsType<ItemMatch.Ambiguous>(match);
        Assert.Equal(2, amb.Candidates.Count);
        // Deterministic order: ascending by Id.
        Assert.Equal(5, amb.Candidates[0].Id);
        Assert.Equal(10, amb.Candidates[1].Id);
    }

    [Fact]
    public void ExactNameCollision_DoesNotFallThroughToSubstring()
    {
        // Even though there are 2 exact matches, the result must be Ambiguous (not a substring-ranked list).
        var a = new ItemRecord(1, "Sunglasses", 1, null, null, null, null, null, null);
        var b = new ItemRecord(2, "Sunglasses", 1, null, null, null, null, null, null);
        var extra = new ItemRecord(3, "Cool Sunglasses", 1, null, null, null, null, null, null);
        IReadOnlyList<ItemRecord> items = [a, b, extra];
        var match = ItemLookup.Resolve("Sunglasses", id => items.FirstOrDefault(i => i.Id == id), items);
        var amb = Assert.IsType<ItemMatch.Ambiguous>(match);
        // Must contain only the 2 exact matches, not the substring hit.
        Assert.Equal(2, amb.Candidates.Count);
    }

    [Fact]
    public void SingleExactMatch_StillReturnsFound()
    {
        var match = Resolve("AK-47");
        var found = Assert.IsType<ItemMatch.Found>(match);
        Assert.Equal("AK-47", found.Item.Name);
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
        var many = Enumerable.Range(1, 50)
            .Select(i => new ItemRecord(i, $"Gun {i}", 1, null, null, null, null, null, null))
            .ToList();
        var match = ItemLookup.Resolve("gun", id => many.FirstOrDefault(x => x.Id == id), many, cap: 10);
        var amb = Assert.IsType<ItemMatch.Ambiguous>(match);
        Assert.Equal(10, amb.Candidates.Count);
    }
}
