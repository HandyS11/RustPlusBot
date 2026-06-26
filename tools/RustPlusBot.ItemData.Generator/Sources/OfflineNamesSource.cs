using System.Globalization;
using System.Text.Json;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Reads item names from the offline <c>items.json</c> file.</summary>
/// <param name="FilePath">Path to the <c>items.json</c> file.</param>
internal sealed class OfflineNamesSource(string FilePath) : INamesSource
{
    /// <inheritdoc/>
    public IReadOnlyDictionary<int, string> LoadNames()
    {
        using var stream = File.OpenRead(FilePath);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<int, string>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            if (prop.Value.TryGetProperty("name", out var nameEl))
            {
                var name = nameEl.GetString();
                if (name is not null)
                {
                    result[id] = name;
                }
            }
        }

        return result;
    }
}
