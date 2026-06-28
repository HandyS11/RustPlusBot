using System.Globalization;
using System.Text.Json;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Reads smelting data from the offline RustLabs JSON file, keyed by smelter id.</summary>
/// <param name="SmeltingFilePath">Path to the <c>rustlabsSmeltingData.json</c> file.</param>
internal sealed class OfflineSmeltingSource(string SmeltingFilePath) : ISmeltingSource
{
    /// <inheritdoc/>
    public IReadOnlyList<Smelter> LoadSmelters(IReadOnlyDictionary<int, string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        using var stream = File.OpenRead(SmeltingFilePath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var smelters = new List<Smelter>();
        foreach (var prop in root.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var smelterId) ||
                !names.TryGetValue(smelterId, out var smelterName))
            {
                continue; // unknown smelter id — drop
            }

            var conversions = ReadConversions(prop.Value, names);
            if (conversions.Count > 0)
            {
                smelters.Add(new Smelter(prop.Name, smelterName, conversions));
            }
        }

        return smelters;
    }

    private static List<SmeltConversion> ReadConversions(JsonElement rows, IReadOnlyDictionary<int, string> names)
    {
        var conversions = new List<SmeltConversion>();
        foreach (var row in rows.EnumerateArray())
        {
            if (ReadId(row, "fromId") is not { } inputId || !names.ContainsKey(inputId) ||
                ReadId(row, "toId") is not { } outputId || !names.ContainsKey(outputId))
            {
                continue; // orphan id with no item name — drop
            }

            conversions.Add(new SmeltConversion(
                inputId,
                outputId,
                ReadInt(row, "toQuantity") ?? 1,
                ReadDouble(row, "toProbability") ?? 1,
                ReadDouble(row, "woodQuantity") ?? 0,
                ReadDouble(row, "time") ?? 0));
        }

        return conversions;
    }

    private static int? ReadId(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.String when int.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var v) => v,
            JsonValueKind.Number => p.GetInt32(),
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

    private static double? ReadDouble(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
}
