using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class OfflineDespawnSourceTests
{
    private static OfflineDespawnSource SourceWith(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return new OfflineDespawnSource(path);
    }

    [Fact]
    public void LoadDespawnSeconds_ParsesTimeAsInteger()
    {
        const string json = """
                            {
                              "317398316": { "time": 300 },
                              "1545779598": { "time": 3600 }
                            }
                            """;
        var result = SourceWith(json).LoadDespawnSeconds();

        Assert.Equal(2, result.Count);
        Assert.Equal(300, result[317398316]);
        Assert.Equal(3600, result[1545779598]);
    }

    [Fact]
    public void LoadDespawnSeconds_SkipsNonIntegerKey()
    {
        const string json = """
                            {
                              "notAnInt": { "time": 600 },
                              "100": { "time": 120 }
                            }
                            """;
        var result = SourceWith(json).LoadDespawnSeconds();

        var (id, time) = Assert.Single(result);
        Assert.Equal(100, id);
        Assert.Equal(120, time);
    }

    [Fact]
    public void LoadDespawnSeconds_SkipsEntryMissingTimeField()
    {
        const string json = """
                            {
                              "200": { "name": "Wood" },
                              "300": { "time": 900 }
                            }
                            """;
        var result = SourceWith(json).LoadDespawnSeconds();

        var (id, time) = Assert.Single(result);
        Assert.Equal(300, id);
        Assert.Equal(900, time);
    }

    [Fact]
    public void LoadDespawnSeconds_SkipsEntryWithStringTime()
    {
        // The "time" field must be a JSON number; a string value is skipped.
        const string json = """{ "100": { "time": "300" } }""";
        Assert.Empty(SourceWith(json).LoadDespawnSeconds());
    }

    [Fact]
    public void LoadDespawnSeconds_EmptyObject_ReturnsEmpty()
    {
        Assert.Empty(SourceWith("{}").LoadDespawnSeconds());
    }
}
