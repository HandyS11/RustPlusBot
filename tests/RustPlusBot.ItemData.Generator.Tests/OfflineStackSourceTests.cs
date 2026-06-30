using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class OfflineStackSourceTests
{
    private static OfflineStackSource SourceWith(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return new OfflineStackSource(path);
    }

    [Fact]
    public void LoadStackSizes_ParsesQuantityString_ReturningIdAndSize()
    {
        const string json = """
                            {
                              "317398316": { "quantity": "1000" },
                              "1545779598": { "quantity": "1" }
                            }
                            """;
        var result = SourceWith(json).LoadStackSizes();

        Assert.Equal(2, result.Count);
        Assert.Equal(1000, result[317398316]);
        Assert.Equal(1, result[1545779598]);
    }

    [Fact]
    public void LoadStackSizes_SkipsNonIntegerKey()
    {
        const string json = """
                            {
                              "notAnInt": { "quantity": "5" },
                              "100": { "quantity": "10" }
                            }
                            """;
        var result = SourceWith(json).LoadStackSizes();

        var (id, qty) = Assert.Single(result);
        Assert.Equal(100, id);
        Assert.Equal(10, qty);
    }

    [Fact]
    public void LoadStackSizes_SkipsEntryMissingQuantityField()
    {
        const string json = """
                            {
                              "200": { "name": "Wood" },
                              "300": { "quantity": "50" }
                            }
                            """;
        var result = SourceWith(json).LoadStackSizes();

        var (id, qty) = Assert.Single(result);
        Assert.Equal(300, id);
        Assert.Equal(50, qty);
    }

    [Fact]
    public void LoadStackSizes_SkipsEntryWithNullQuantity()
    {
        const string json = """{ "100": { "quantity": null } }""";
        Assert.Empty(SourceWith(json).LoadStackSizes());
    }

    [Fact]
    public void LoadStackSizes_SkipsEntryWithNonNumericQuantityString()
    {
        const string json = """{ "100": { "quantity": "many" } }""";
        Assert.Empty(SourceWith(json).LoadStackSizes());
    }
}
