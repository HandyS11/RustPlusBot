using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace RustPlusBot.Features.StorageMonitors.Naming;

/// <summary>Resolves item ids from the bundled <c>items.json</c> (Facepunch's published item names). Singleton.</summary>
public sealed class EmbeddedItemNameResolver : IItemNameResolver
{
    private static readonly FrozenDictionary<int, string> Names = Load();

    /// <inheritdoc />
    public string Resolve(int itemId) =>
        Names.TryGetValue(itemId, out var name)
            ? name
            : "Item " + itemId.ToString(CultureInfo.InvariantCulture);

    private static FrozenDictionary<int, string> Load()
    {
        var assembly = typeof(EmbeddedItemNameResolver).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("items.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException("Embedded items.json not found.");
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                  ?? throw new InvalidOperationException("items.json deserialized to null.");
        // Group by the parsed id (last wins) before freezing: two distinct string keys can parse to the
        // same int (NumberStyles.Integer allows leading sign/whitespace), and ToFrozenDictionary throws on a
        // duplicate key — which, in a static initializer, would hard-fault the feature. Degrade, never crash.
        return raw
            .Select(kvp => (Parsed: int.TryParse(kvp.Key, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var id), Id: id, kvp.Value))
            .Where(x => x.Parsed)
            .GroupBy(x => x.Id, x => x.Value)
            .ToFrozenDictionary(g => g.Key, g => g.Last());
    }
}
