using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class OfflineRustLabsSourceTests
{
    private static OfflineRustLabsSource SourceWith(string decayJson, string upkeepJson)
    {
        var decayPath = Path.GetTempFileName();
        var upkeepPath = Path.GetTempFileName();
        File.WriteAllText(decayPath, decayJson);
        File.WriteAllText(upkeepPath, upkeepJson);
        // Recycle/craft/research paths are unused by these two loaders; point them at the upkeep temp file.
        return new OfflineRustLabsSource(upkeepPath, upkeepPath, upkeepPath, decayPath, upkeepPath);
    }

    [Fact]
    public void LoadDecay_readsItemsWrapper_andSecondsAndHp()
    {
        const string decay =
            """{"items":{"15388698":{"decay":900,"decayString":"15 min","decayOutside":null,"decayInside":null,"decayUnderwater":null,"hp":100,"hpString":"100"}}}""";
        var result = SourceWith(decay, """{"items":{}}""").LoadDecay();

        Assert.True(result.ContainsKey(15388698));
        Assert.Equal(900, result[15388698].Seconds);
        Assert.Equal(100, result[15388698].Hp);
        Assert.Null(result[15388698].OutsideSeconds);
    }

    [Fact]
    public void LoadUpkeep_readsItemsWrapper_andParsesRange()
    {
        const string upkeep =
            """{"items":{"1729120840":[{"id":"-151838493","quantity":"8–25"}]}}""";
        var result = SourceWith("""{"items":{}}""", upkeep).LoadUpkeep();

        Assert.True(result.ContainsKey(1729120840));
        var entry = Assert.Single(result[1729120840].Entries);
        Assert.Equal(-151838493, entry.ItemId);
        Assert.Equal(8, entry.QuantityMin);
        Assert.Equal(25, entry.QuantityMax);
    }

    [Fact]
    public void LoadUpkeep_singleQuantity_hasEqualMinMax()
    {
        const string upkeep = """{"items":{"671706427":[{"id":"317398316","quantity":"1"}]}}""";
        var entry = Assert.Single(SourceWith("""{"items":{}}""", upkeep).LoadUpkeep()[671706427].Entries);
        Assert.Equal(1, entry.QuantityMin);
        Assert.Equal(1, entry.QuantityMax);
    }
}
