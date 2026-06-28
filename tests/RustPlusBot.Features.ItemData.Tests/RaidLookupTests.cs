using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class RaidLookupTests
{
    private static readonly RaidTarget Wall = new("Stone Wall", "Stone Wall", RaidTargetKind.BuildingBlock, []);

    private static readonly RaidTarget Door = new("Sheet Metal Door", "Sheet Metal Door", RaidTargetKind.BuildingBlock,
        []);

    private static readonly RaidTarget Tc = new("100", "Tool Cupboard", RaidTargetKind.Item, []);
    private static readonly IReadOnlyList<RaidTarget> All = [Wall, Door, Tc];

    private static RaidTarget? ById(int id) =>
        All.FirstOrDefault(t => t.Kind == RaidTargetKind.Item && int.TryParse(t.Key, out var k) && k == id);

    private static RaidMatch Resolve(string q) => RaidLookup.Resolve(q, ById, All);

    [Fact]
    public void NumericInput_ResolvesItemTargetById()
    {
        var found = Assert.IsType<RaidMatch.Found>(Resolve("100"));
        Assert.Equal("Tool Cupboard", found.Target.Name);
    }

    [Fact]
    public void ExactBlockName_Found()
    {
        var found = Assert.IsType<RaidMatch.Found>(Resolve("stone wall"));
        Assert.Equal("Stone Wall", found.Target.Name);
    }

    [Fact]
    public void Substring_Found()
    {
        var found = Assert.IsType<RaidMatch.Found>(Resolve("cupboard"));
        Assert.Equal("Tool Cupboard", found.Target.Name);
    }

    [Fact]
    public void MultipleSubstring_Ambiguous()
    {
        var amb = Assert.IsType<RaidMatch.Ambiguous>(Resolve("o")); // Stone, Door, Tool all contain 'o'
        Assert.True(amb.Candidates.Count >= 2);
    }

    [Fact]
    public void NoMatch_NotFound() => Assert.IsType<RaidMatch.NotFound>(Resolve("zzzzz"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_NotFound(string q) => Assert.IsType<RaidMatch.NotFound>(Resolve(q));
}
