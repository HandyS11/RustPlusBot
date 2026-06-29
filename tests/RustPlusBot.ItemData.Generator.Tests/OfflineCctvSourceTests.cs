using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class OfflineCctvSourceTests
{
    private static OfflineCctvSource SourceWith(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return new OfflineCctvSource(path);
    }

    [Fact]
    public void LoadMonuments_ProjectsCodesInOrder_AndDynamicFlag()
    {
        const string json = """
                            {
                              "Small Oil Rig": { "codes": ["OILRIG1HELI", "OILRIG1DOCK"], "dynamic": false }
                            }
                            """;
        var monument = Assert.Single(SourceWith(json).LoadMonuments());
        Assert.Equal("Small Oil Rig", monument.Name);
        Assert.Equal(["OILRIG1HELI", "OILRIG1DOCK"], monument.Codes);
        Assert.False(monument.Dynamic);
    }

    [Fact]
    public void LoadMonuments_UnescapesAsterisks_PreservingCount()
    {
        const string json = """
                            {
                              "Underwater Labs": { "codes": ["AUXPOWER\\*\\*\\*\\*"], "dynamic": true }
                            }
                            """;
        var monument = Assert.Single(SourceWith(json).LoadMonuments());
        Assert.True(monument.Dynamic);
        Assert.Equal("AUXPOWER****", monument.Codes[0]);
    }

    [Fact]
    public void LoadMonuments_DropsMonumentWithNoCodes()
    {
        const string json = """{ "Empty": { "codes": [], "dynamic": false } }""";
        Assert.Empty(SourceWith(json).LoadMonuments());
    }
}
