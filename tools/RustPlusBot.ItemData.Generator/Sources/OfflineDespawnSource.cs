using System.Globalization;
using System.Text.Json;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Reads despawn times from the offline <c>rustlabsDespawnData.json</c> file.</summary>
/// <param name="FilePath">Path to the <c>rustlabsDespawnData.json</c> file.</param>
internal sealed class OfflineDespawnSource(string FilePath) : IDespawnSource
{
    /// <inheritdoc/>
    public IReadOnlyDictionary<int, int> LoadDespawnSeconds()
    {
        using var stream = File.OpenRead(FilePath);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<int, int>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            if (prop.Value.TryGetProperty("time", out var timeEl) &&
                timeEl.ValueKind == JsonValueKind.Number &&
                timeEl.TryGetInt32(out var time))
            {
                result[id] = time;
            }
        }

        return result;
    }
}
