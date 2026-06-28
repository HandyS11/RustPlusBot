using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class SmeltLookupTests
{
    private static readonly Smelter Furnace = new("100", "Furnace", []);
    private static readonly Smelter Large = new("101", "Large Furnace", []);
    private static readonly Smelter Camp = new("102", "Camp Fire", []);
    private static readonly IReadOnlyList<Smelter> All = [Furnace, Large, Camp];

    private static Smelter? ById(int id) => All.FirstOrDefault(s => int.TryParse(s.Key, out var k) && k == id);

    private static SmeltMatch Resolve(string q) => SmeltLookup.Resolve(q, ById, All);

    [Fact]
    public void NumericInput_ResolvesById()
    {
        var found = Assert.IsType<SmeltMatch.Found>(Resolve("102"));
        Assert.Equal("Camp Fire", found.Smelter.Name);
    }

    [Fact]
    public void ExactName_BeatsSubstring()
    {
        var found = Assert.IsType<SmeltMatch.Found>(Resolve("furnace")); // exact "Furnace" wins over "Large Furnace"
        Assert.Equal("Furnace", found.Smelter.Name);
    }

    [Fact]
    public void UniqueSubstring_Found()
    {
        var found = Assert.IsType<SmeltMatch.Found>(Resolve("camp"));
        Assert.Equal("Camp Fire", found.Smelter.Name);
    }

    [Fact]
    public void MultipleSubstring_Ambiguous()
    {
        var amb = Assert.IsType<SmeltMatch.Ambiguous>(Resolve("fur")); // Furnace + Large Furnace both contain "fur"
        Assert.True(amb.Candidates.Count >= 2);
    }

    [Fact]
    public void NoMatch_NotFound() => Assert.IsType<SmeltMatch.NotFound>(Resolve("zzzzz"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_NotFound(string q) => Assert.IsType<SmeltMatch.NotFound>(Resolve(q));
}
