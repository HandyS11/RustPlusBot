using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class OfflineSmeltingSourceTests
{
    private static OfflineSmeltingSource SourceWith(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return new OfflineSmeltingSource(path);
    }

    [Fact]
    public void LoadSmelters_ProjectsConversions_ResolvingSmelterNameAndIds()
    {
        const string json = """
                            {
                              "100": [
                                {"fromId":"10","woodQuantity":1.67,"toId":"20","toQuantity":1,"toProbability":1,"time":3.33,"timeString":"3.33 sec"},
                                {"fromId":"11","woodQuantity":1,"toId":"21","toQuantity":1,"toProbability":0.75,"time":2,"timeString":"2 sec"}
                              ]
                            }
                            """;
        var names = new Dictionary<int, string>
        {
            [100] = "Furnace", [10] = "Metal Ore", [20] = "Metal Fragments", [11] = "Wood", [21] = "Charcoal",
        };

        var smelters = SourceWith(json).LoadSmelters(names);

        var furnace = Assert.Single(smelters);
        Assert.Equal("Furnace", furnace.Name);
        Assert.Equal("100", furnace.Key);
        Assert.Equal(2, furnace.Conversions.Count);
        var first = furnace.Conversions[0];
        Assert.Equal(10, first.InputId);
        Assert.Equal(20, first.OutputId);
        Assert.Equal(1, first.OutputQuantity);
        Assert.Equal(1.67, first.WoodQuantity);
        Assert.Equal(3.33, first.TimeSeconds);
        Assert.Equal(0.75, furnace.Conversions[1].OutputProbability);
    }

    [Fact]
    public void LoadSmelters_DropsSmelterWithUnknownId()
    {
        const string json =
            """{"999":[{"fromId":"10","woodQuantity":1,"toId":"20","toQuantity":1,"toProbability":1,"time":2}]}""";
        var names = new Dictionary<int, string> { [10] = "Metal Ore", [20] = "Metal Fragments" };
        Assert.Empty(SourceWith(json).LoadSmelters(names));
    }

    [Fact]
    public void LoadSmelters_DropsConversionWithUnknownInputOrOutput()
    {
        const string json =
            """{"100":[{"fromId":"10","woodQuantity":1,"toId":"999","toQuantity":1,"toProbability":1,"time":2}]}""";
        var names = new Dictionary<int, string> { [100] = "Furnace", [10] = "Metal Ore" };
        Assert.Empty(SourceWith(json).LoadSmelters(names)); // smelter dropped: no valid conversions remain
    }
}
