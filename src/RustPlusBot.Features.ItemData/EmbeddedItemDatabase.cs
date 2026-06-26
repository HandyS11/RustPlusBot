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

    private static readonly FrozenDictionary<int, ItemRecord> ById = IndexById(Dataset.Items);

    /// <inheritdoc />
    public DatasetSources Sources => Dataset.Sources;

    /// <inheritdoc />
    public ItemRecord? GetById(int id) => ById.GetValueOrDefault(id);

    /// <inheritdoc />
    public ItemMatch Resolve(string query) => ItemLookup.Resolve(query, GetById, Dataset.Items);

    /// <summary>
    /// Deserializes and validates an item dataset from <paramref name="stream"/>.
    /// Throws <see cref="InvalidOperationException"/> when the schema version does not match
    /// <paramref name="expectedSchemaVersion"/>.
    /// </summary>
    /// <param name="stream">A readable stream containing the JSON dataset.</param>
    /// <param name="expectedSchemaVersion">The schema version the caller expects.</param>
    /// <returns>The validated <see cref="ItemDataset"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The stream deserializes to null, or the schema version does not match <paramref name="expectedSchemaVersion"/>.
    /// </exception>
    internal static ItemDataset Parse(Stream stream, int expectedSchemaVersion)
    {
        var dataset = JsonSerializer.Deserialize<ItemDataset>(stream, JsonOptions)
                      ?? throw new InvalidOperationException("item-data.json deserialized to null.");
        if (dataset.SchemaVersion != expectedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"item-data.json schema version {dataset.SchemaVersion} != expected {expectedSchemaVersion}.");
        }

        return dataset;
    }

    /// <summary>
    /// Builds a frozen id→item index from <paramref name="items"/>.
    /// When the same id appears more than once the last entry wins.
    /// </summary>
    /// <param name="items">The full item list.</param>
    /// <returns>A frozen dictionary keyed by item id.</returns>
    internal static FrozenDictionary<int, ItemRecord> IndexById(IReadOnlyList<ItemRecord> items) =>
        items.GroupBy(i => i.Id).ToFrozenDictionary(g => g.Key, g => g.Last());

    private static ItemDataset Load()
    {
        var assembly = typeof(EmbeddedItemDatabase).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("item-data.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException("Embedded item-data.json not found.");
        return Parse(stream, ExpectedSchemaVersion);
    }
}
