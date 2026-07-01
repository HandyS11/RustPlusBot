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

    private static OfflineRustLabsSource SourceForRecycle(string recycleJson)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, recycleJson);
        return new OfflineRustLabsSource(path, path, path, path, path);
    }

    [Fact]
    public void LoadRecycleYields_parsesEntries()
    {
        const string json = """{"100":{"recycler":{"yield":[{"id":"200","quantity":4,"probability":1.0}]}}}""";
        var result = SourceForRecycle(json).LoadRecycleYields();
        var yield = Assert.Single(result[100].Recycler);
        Assert.Equal(200, yield.ItemId);
        Assert.Equal(4, yield.Quantity);
    }

    [Fact]
    public void LoadRecycleYields_skipsNullRecycler()
    {
        const string json = """{"100":{"recycler":null}}""";
        Assert.Empty(SourceForRecycle(json).LoadRecycleYields());
    }

    [Fact]
    public void LoadCraftRecipes_parsesIngredientsTimeAndWorkbench()
    {
        const string json =
            """{"100":{"ingredients":[{"id":"200","quantity":50}],"time":30,"workbench":"-41896755"}}""";
        var result = SourceForRecycle(json).LoadCraftRecipes();
        var recipe = result[100];
        var ing = Assert.Single(recipe.Ingredients);
        Assert.Equal(200, ing.ItemId);
        Assert.Equal(50, ing.Quantity);
        Assert.Equal(2, recipe.WorkbenchLevel); // "-41896755" maps to workbench 2
    }

    [Fact]
    public void LoadCraftRecipes_skipsRecipeWithNoIngredients()
    {
        const string json = """{"100":{"ingredients":[],"time":30}}""";
        Assert.Empty(SourceForRecycle(json).LoadCraftRecipes());
    }

    [Fact]
    public void LoadResearchCosts_parsesScrap()
    {
        const string json = """{"100":{"researchTable":75}}""";
        var result = SourceForRecycle(json).LoadResearchCosts();
        Assert.Equal(75, result[100].Scrap);
    }
}
