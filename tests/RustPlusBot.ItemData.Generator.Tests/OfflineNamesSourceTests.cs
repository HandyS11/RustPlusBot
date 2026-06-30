using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class OfflineNamesSourceTests
{
    private static OfflineNamesSource SourceWith(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return new OfflineNamesSource(path);
    }

    [Fact]
    public void LoadNames_ParsesIdAndNameString()
    {
        const string json = """
                            {
                              "317398316": { "name": "High Quality Metal" },
                              "-151838493": { "name": "Wood" }
                            }
                            """;
        var result = SourceWith(json).LoadNames();

        Assert.Equal(2, result.Count);
        Assert.Equal("High Quality Metal", result[317398316]);
        Assert.Equal("Wood", result[-151838493]);
    }

    [Fact]
    public void LoadNames_SkipsNonIntegerKey()
    {
        const string json = """
                            {
                              "notAnInt": { "name": "Ghost Item" },
                              "100": { "name": "Real Item" }
                            }
                            """;
        var result = SourceWith(json).LoadNames();

        var (id, name) = Assert.Single(result);
        Assert.Equal(100, id);
        Assert.Equal("Real Item", name);
    }

    [Fact]
    public void LoadNames_SkipsEntryMissingNameField()
    {
        const string json = """
                            {
                              "200": { "quantity": "50" },
                              "300": { "name": "Cloth" }
                            }
                            """;
        var result = SourceWith(json).LoadNames();

        var (id, name) = Assert.Single(result);
        Assert.Equal(300, id);
        Assert.Equal("Cloth", name);
    }

    [Fact]
    public void LoadNames_SkipsEntryWithNullName()
    {
        const string json = """{ "100": { "name": null } }""";
        Assert.Empty(SourceWith(json).LoadNames());
    }

    [Fact]
    public void LoadNames_EmptyObject_ReturnsEmpty()
    {
        Assert.Empty(SourceWith("{}").LoadNames());
    }
}
