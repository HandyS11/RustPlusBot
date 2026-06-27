using System.Text;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class EmbeddedItemDatabaseTests
{
    private readonly EmbeddedItemDatabase _db = new();

    [Fact]
    public void GetById_ReturnsKnownItem()
    {
        var ak = _db.GetById(1545779598);
        Assert.NotNull(ak);
        Assert.Equal("Assault Rifle", ak!.Name);
    }

    [Fact]
    public void GetById_ReturnsNullForUnknown()
    {
        Assert.Null(_db.GetById(123456789));
    }

    [Fact]
    public void Sources_ArePopulated()
    {
        Assert.Equal(new DateOnly(2026, 4, 8), _db.Sources.NamesAsOf);
    }

    [Fact]
    public void Parse_ThrowsOnSchemaVersionMismatch()
    {
        const string json =
            """{"schemaVersion":999,"sources":{"namesAsOf":"2026-04-08","recycleAsOf":"2024-09-07","craftAsOf":"2024-09-07","researchAsOf":"2024-09-07"},"items":[]}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<InvalidOperationException>(() => EmbeddedItemDatabase.Parse(stream, 1));
    }

    [Fact]
    public void Parse_SucceedsOnMatchingSchemaVersion()
    {
        const string json =
            """{"schemaVersion":1,"sources":{"namesAsOf":"2026-04-08","recycleAsOf":"2024-09-07","craftAsOf":"2024-09-07","researchAsOf":"2024-09-07"},"items":[]}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var dataset = EmbeddedItemDatabase.Parse(stream, 1);
        Assert.Equal(1, dataset.SchemaVersion);
    }

    [Fact]
    public void IndexById_DuplicateId_LastWins()
    {
        var first = new ItemRecord(42, "First", 1, null, null, null, null, null, null);
        var last = new ItemRecord(42, "Last", 1, null, null, null, null, null, null);
        var index = EmbeddedItemDatabase.IndexById([first, last]);
        Assert.Equal("Last", index[42].Name);
    }

    [Fact]
    public void GetById_StoneBarricade_HasDecay()
    {
        var rec = _db.GetById(15388698);
        Assert.NotNull(rec);
        Assert.NotNull(rec!.Decay);
        Assert.Equal(900, rec.Decay!.Seconds);
        Assert.Equal(100, rec.Decay.Hp);
    }

    [Fact]
    public void GetById_WoodenDoor_HasUpkeep()
    {
        var rec = _db.GetById(1729120840);
        Assert.NotNull(rec);
        Assert.NotNull(rec!.Upkeep);
        Assert.NotEmpty(rec.Upkeep!.Entries);
    }

    [Fact]
    public void Sources_DecayAndUpkeep_ArePopulated()
    {
        Assert.Equal(new DateOnly(2024, 9, 7), _db.Sources.DecayAsOf);
        Assert.Equal(new DateOnly(2024, 9, 7), _db.Sources.UpkeepAsOf);
    }
}
