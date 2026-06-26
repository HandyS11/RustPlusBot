using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;

namespace RustPlusBot.Features.ItemData;

/// <summary>Loads the embedded <c>item-data.json</c> once and serves lookups. Singleton.</summary>
public sealed class EmbeddedItemDatabase : IItemDatabase
{
    private const int ExpectedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly ItemDataset Dataset = Load();

    private static readonly FrozenDictionary<int, ItemRecord> ById =
        Dataset.Items.GroupBy(i => i.Id).ToFrozenDictionary(g => g.Key, g => g.Last());

    /// <inheritdoc />
    public DatasetSources Sources => Dataset.Sources;

    /// <inheritdoc />
    public ItemRecord? GetById(int id) => ById.GetValueOrDefault(id);

    /// <inheritdoc />
    public ItemMatch Resolve(string query) => ItemLookup.Resolve(query, GetById, Dataset.Items);

    private static ItemDataset Load()
    {
        var assembly = typeof(EmbeddedItemDatabase).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("item-data.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException("Embedded item-data.json not found.");
        var dataset = JsonSerializer.Deserialize<ItemDataset>(stream, JsonOptions)
                      ?? throw new InvalidOperationException("item-data.json deserialized to null.");
        if (dataset.SchemaVersion != ExpectedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"item-data.json schema version {dataset.SchemaVersion} != expected {ExpectedSchemaVersion}.");
        }

        return dataset;
    }
}
