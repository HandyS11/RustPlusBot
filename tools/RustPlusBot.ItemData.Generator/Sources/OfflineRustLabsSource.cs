using System.Globalization;
using System.Text.Json;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Reads recycle, craft, research, decay, and upkeep data from the offline RustLabs JSON files.</summary>
/// <param name="RecycleFilePath">Path to the <c>rustlabsRecycleData.json</c> file.</param>
/// <param name="CraftFilePath">Path to the <c>rustlabsCraftData.json</c> file.</param>
/// <param name="ResearchFilePath">Path to the <c>rustlabsResearchData.json</c> file.</param>
/// <param name="DecayFilePath">Path to the <c>rustlabsDecayData.json</c> file.</param>
/// <param name="UpkeepFilePath">Path to the <c>rustlabsUpkeepData.json</c> file.</param>
internal sealed class OfflineRustLabsSource(
    string RecycleFilePath,
    string CraftFilePath,
    string ResearchFilePath,
    string DecayFilePath,
    string UpkeepFilePath) : IRustLabsSource
{
    private static readonly Dictionary<string, int> WorkbenchLevels = new()
    {
        ["1524187186"] = 1, ["-41896755"] = 2, ["-1607980696"] = 3,
    };

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, RecycleYield> LoadRecycleYields()
    {
        using var stream = File.OpenRead(RecycleFilePath);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<int, RecycleYield>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            if (!prop.Value.TryGetProperty("recycler", out var recyclerEl) ||
                recyclerEl.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (!recyclerEl.TryGetProperty("yield", out var yieldEl) ||
                yieldEl.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var entries = new List<YieldEntry>();
            foreach (var entry in yieldEl.EnumerateArray())
            {
                var entryIdStr = entry.GetProperty("id").GetString();
                if (entryIdStr is null ||
                    !int.TryParse(entryIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId))
                {
                    continue;
                }

                var probability = entry.GetProperty("probability").GetDouble();
                var quantity = entry.GetProperty("quantity").GetInt32();
                entries.Add(new YieldEntry(entryId, quantity, probability));
            }

            if (entries.Count > 0)
            {
                result[id] = new RecycleYield(entries);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, CraftRecipe> LoadCraftRecipes()
    {
        using var stream = File.OpenRead(CraftFilePath);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<int, CraftRecipe>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            if (!prop.Value.TryGetProperty("ingredients", out var ingredientsEl) ||
                ingredientsEl.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var ingredients = new List<Ingredient>();
            foreach (var entry in ingredientsEl.EnumerateArray())
            {
                var entryIdStr = entry.GetProperty("id").GetString();
                if (entryIdStr is null ||
                    !int.TryParse(entryIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId))
                {
                    continue;
                }

                var quantity = entry.GetProperty("quantity").GetInt32();
                ingredients.Add(new Ingredient(entryId, quantity));
            }

            if (ingredients.Count == 0)
            {
                continue;
            }

            var timeEl = prop.Value.GetProperty("time");
            if (timeEl.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var timeSeconds = timeEl.GetDouble();

            int? workbenchLevel = null;
            if (prop.Value.TryGetProperty("workbench", out var workbenchEl) &&
                workbenchEl.ValueKind == JsonValueKind.String)
            {
                var wbStr = workbenchEl.GetString();
                if (wbStr is not null && WorkbenchLevels.TryGetValue(wbStr, out var level))
                {
                    workbenchLevel = level;
                }
            }

            result[id] = new CraftRecipe(ingredients, timeSeconds, workbenchLevel);
        }

        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, ResearchCost> LoadResearchCosts()
    {
        using var stream = File.OpenRead(ResearchFilePath);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<int, ResearchCost>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            if (!prop.Value.TryGetProperty("researchTable", out var researchTableEl) ||
                researchTableEl.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (researchTableEl.TryGetInt32(out var scrap))
            {
                result[id] = new ResearchCost(scrap);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, DecayInfo> LoadDecay()
    {
        using var stream = File.OpenRead(DecayFilePath);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<int, DecayInfo>();
        if (!doc.RootElement.TryGetProperty("items", out var itemsEl))
        {
            return result;
        }

        foreach (var prop in itemsEl.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            result[id] = new DecayInfo(
                ReadInt(prop.Value, "decay"),
                ReadInt(prop.Value, "decayOutside"),
                ReadInt(prop.Value, "decayInside"),
                ReadInt(prop.Value, "decayUnderwater"),
                ReadInt(prop.Value, "hp"));
        }

        return result;

        static int? ReadInt(JsonElement element, string name) =>
            element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number
                ? p.GetInt32()
                : null;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, UpkeepCost> LoadUpkeep()
    {
        using var stream = File.OpenRead(UpkeepFilePath);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<int, UpkeepCost>();
        if (!doc.RootElement.TryGetProperty("items", out var itemsEl))
        {
            return result;
        }

        foreach (var prop in itemsEl.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            var entries = new List<UpkeepEntry>();
            foreach (var entry in prop.Value.EnumerateArray())
            {
                var entryIdStr = entry.GetProperty("id").GetString();
                if (entryIdStr is null ||
                    !int.TryParse(entryIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId))
                {
                    continue;
                }

                var quantity = entry.GetProperty("quantity").GetString()
                               ?? throw new InvalidOperationException($"upkeep {id}: null quantity");
                var (min, max) = UpkeepQuantity.Parse(quantity);
                entries.Add(new UpkeepEntry(entryId, min, max));
            }

            if (entries.Count > 0)
            {
                result[id] = new UpkeepCost(entries);
            }
        }

        return result;
    }
}
