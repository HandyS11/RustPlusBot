using System.Text.Json;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.ItemData.Generator.Sources;
using RustPlusBot.ItemData.Generator.Validation;

namespace RustPlusBot.ItemData.Generator;

/// <summary>Entry point for the item-data generator tool.</summary>
internal static class Program
{
    private static readonly DateOnly NamesAsOf = new(2026, 4, 8);
    private static readonly DateOnly RecycleAsOf = new(2024, 9, 7);
    private static readonly DateOnly CraftAsOf = new(2024, 9, 7);
    private static readonly DateOnly ResearchAsOf = new(2024, 9, 7);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>Main entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>0 on success, 1 on validation failure.</returns>
    internal static int Main(string[] args)
    {
        string? outPath = null;
        var rustplusDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Dev/rustplusplus/src/staticFiles");
        var minItems = 1000;

        var argList = args.ToList();
        var outIdx = argList.IndexOf("--out");
        if (outIdx >= 0 && outIdx + 1 < argList.Count)
        {
            outPath = argList[outIdx + 1];
        }

        var rustIdx = argList.IndexOf("--rustplusplus");
        if (rustIdx >= 0 && rustIdx + 1 < argList.Count)
        {
            rustplusDir = ExpandHome(argList[rustIdx + 1]);
        }

        var minIdx = argList.IndexOf("--min-items");
        if (minIdx >= 0 && minIdx + 1 < argList.Count)
        {
            minItems = int.Parse(argList[minIdx + 1], System.Globalization.CultureInfo.InvariantCulture);
        }

        if (outPath is null)
        {
            Console.Error.WriteLine("Usage: generator --out <path> [--rustplusplus <dir>] [--min-items <n>]");
            return 1;
        }

        var namesSource = new OfflineNamesSource(Path.Combine(rustplusDir, "items.json"));
        var stackSource = new OfflineStackSource(Path.Combine(rustplusDir, "rustlabsStackData.json"));
        var despawnSource = new OfflineDespawnSource(Path.Combine(rustplusDir, "rustlabsDespawnData.json"));
        var rustLabsSource = new OfflineRustLabsSource(
            Path.Combine(rustplusDir, "rustlabsRecycleData.json"),
            Path.Combine(rustplusDir, "rustlabsCraftData.json"),
            Path.Combine(rustplusDir, "rustlabsResearchData.json"),
            Path.Combine(rustplusDir, "rustlabsDecayData.json"),
            Path.Combine(rustplusDir, "rustlabsUpkeepData.json"));

        var names = namesSource.LoadNames();
        Console.WriteLine($"Loaded {names.Count} names from items.json");

        var stackSizes = stackSource.LoadStackSizes();
        var despawnSeconds = despawnSource.LoadDespawnSeconds();
        var recycleYields = rustLabsSource.LoadRecycleYields();
        var craftRecipes = rustLabsSource.LoadCraftRecipes();
        var researchCosts = rustLabsSource.LoadResearchCosts();

        var nameIds = new HashSet<int>(names.Keys);

        var orphanRecycle = recycleYields.Keys.Count(k => !nameIds.Contains(k));
        var orphanCraft = craftRecipes.Keys.Count(k => !nameIds.Contains(k));
        var orphanResearch = researchCosts.Keys.Count(k => !nameIds.Contains(k));

        Console.WriteLine($"dropped {orphanRecycle} orphan recycle entries with no item name");
        Console.WriteLine($"dropped {orphanCraft} orphan craft entries with no item name");
        Console.WriteLine($"dropped {orphanResearch} orphan research entries with no item name");

        var items = names
            .Select(kv =>
            {
                var id = kv.Key;
                var name = kv.Value;
                var stackSize = stackSizes.TryGetValue(id, out var ss) ? ss : 1;
                var despawn = despawnSeconds.TryGetValue(id, out var ds) ? (int?)ds : null;
                var recycle = recycleYields.TryGetValue(id, out var ry) ? ry : null;
                var craft = craftRecipes.TryGetValue(id, out var cr) ? cr : null;
                var research = researchCosts.TryGetValue(id, out var rc) ? rc : null;
                return new ItemRecord(id, name, stackSize, despawn, recycle, craft, research, null, null);
            })
            .ToList();

        var dataset = new ItemDataset(
            1,
            new DatasetSources(NamesAsOf, RecycleAsOf, CraftAsOf, ResearchAsOf, new(2024, 9, 7), new(2024, 9, 7)),
            items);

        var validationOptions = new ValidationOptions(MinItemCount: minItems);
        var errors = DatasetValidator.Validate(dataset, validationOptions);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                Console.Error.WriteLine($"Validation error: {error}");
            }

            return 1;
        }

        var json = JsonSerializer.Serialize(dataset, JsonOptions);
        File.WriteAllText(outPath, json);
        Console.WriteLine($"Emitted {items.Count} items to {outPath}");
        return 0;
    }

    private static string ExpandHome(string path)
    {
        if (path.StartsWith("~/", StringComparison.Ordinal) || path == "~")
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[2..]);
        }

        return path;
    }
}
