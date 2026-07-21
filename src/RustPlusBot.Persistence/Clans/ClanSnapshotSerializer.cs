using System.Text.Json;
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Persistence.Clans;

/// <summary>
/// Serializes the clan's collections to and from the JSON columns on <c>ClanState</c>. The
/// collections are only ever read and written whole (diff, then render) and nothing queries
/// across them, so three normalised tables would add migration burden for no query benefit.
/// </summary>
internal static class ClanSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes a collection to its JSON column value.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The items to serialize.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize<T>(IReadOnlyList<T> items) => JsonSerializer.Serialize(items, Options);

    /// <summary>Deserializes a JSON column value, yielding an empty list when the text is unusable.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="json">The stored JSON text.</param>
    /// <returns>The deserialized items, or an empty list.</returns>
    public static IReadOnlyList<T> Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            // A corrupted column must not crash the bot; an empty list re-reads as a full change set.
            return [];
        }
    }
}
