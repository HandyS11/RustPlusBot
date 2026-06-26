using System.Globalization;
using System.Text.Json;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Reads stack sizes from the offline <c>rustlabsStackData.json</c> file.</summary>
/// <param name="FilePath">Path to the <c>rustlabsStackData.json</c> file.</param>
internal sealed class OfflineStackSource(string FilePath) : IStackSource
{
    /// <inheritdoc/>
    public IReadOnlyDictionary<int, int> LoadStackSizes()
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

            if (!prop.Value.TryGetProperty("quantity", out var quantityEl))
            {
                continue;
            }

            var quantityStr = quantityEl.GetString();
            if (quantityStr is not null &&
                int.TryParse(quantityStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
            {
                result[id] = quantity;
            }
        }

        return result;
    }
}
