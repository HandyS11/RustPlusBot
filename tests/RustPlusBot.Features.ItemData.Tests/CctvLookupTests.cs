using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class CctvLookupTests
{
    private static readonly CctvMonument Dome = new("Dome", ["DOME1"], false);
    private static readonly CctvMonument SmallRig = new("Small Oil Rig", ["OILRIG1HELI"], false);
    private static readonly CctvMonument LargeRig = new("Large Oil Rig", ["OILRIG2HELI"], false);
    private static readonly IReadOnlyList<CctvMonument> All = [Dome, SmallRig, LargeRig];

    private static CctvMatch Resolve(string q) => CctvLookup.Resolve(q, All);

    [Fact]
    public void ExactName_Found()
    {
        var found = Assert.IsType<CctvMatch.Found>(Resolve("Dome"));
        Assert.Equal("Dome", found.Monument.Name);
    }

    [Fact]
    public void UniqueSubstring_Found()
    {
        var found = Assert.IsType<CctvMatch.Found>(Resolve("small"));
        Assert.Equal("Small Oil Rig", found.Monument.Name);
    }

    [Fact]
    public void MultipleSubstring_Ambiguous()
    {
        var amb = Assert.IsType<CctvMatch.Ambiguous>(Resolve("oil rig"));
        Assert.True(amb.Candidates.Count >= 2);
    }

    [Fact]
    public void NoMatch_NotFound() => Assert.IsType<CctvMatch.NotFound>(Resolve("zzzzz"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_NotFound(string q) => Assert.IsType<CctvMatch.NotFound>(Resolve(q));
}
