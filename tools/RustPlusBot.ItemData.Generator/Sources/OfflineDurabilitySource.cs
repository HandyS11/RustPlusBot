using System.Globalization;
using System.Text.Json;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Reads raid durability data from the offline RustLabs JSON file, keeping only explosives.</summary>
/// <param name="DurabilityFilePath">Path to the <c>rustlabsDurabilityData.json</c> file.</param>
internal sealed class OfflineDurabilitySource(string DurabilityFilePath) : IDurabilitySource
{
    private const string RaidGroup = "explosive";

    /// <inheritdoc/>
    public IReadOnlyList<RaidTarget> LoadRaidTargets(IReadOnlyDictionary<int, string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        using var stream = File.OpenRead(DurabilityFilePath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var targets = new List<RaidTarget>();
        AddSection(root, "items", RaidTargetKind.Item, names, targets);
        AddSection(root, "buildingBlocks", RaidTargetKind.BuildingBlock, names, targets);
        AddSection(root, "other", RaidTargetKind.Vehicle, names, targets);
        return targets;
    }

    private static void AddSection(JsonElement root,
        string section,
        RaidTargetKind kind,
        IReadOnlyDictionary<int, string> names,
        List<RaidTarget> into)
    {
        if (!root.TryGetProperty(section, out var sectionEl) || sectionEl.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var prop in sectionEl.EnumerateObject())
        {
            string key;
            string name;
            if (kind == RaidTargetKind.Item)
            {
                if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                    !names.TryGetValue(id, out var itemName))
                {
                    continue; // orphan id with no item name — drop
                }

                key = prop.Name;
                name = itemName;
            }
            else
            {
                key = prop.Name;
                name = prop.Name;
            }

            var costs = ReadCosts(prop.Value);
            if (costs.Count > 0)
            {
                into.Add(new RaidTarget(key, name, kind, costs));
            }
        }
    }

    private static List<RaidCost> ReadCosts(JsonElement rows)
    {
        var costs = new List<RaidCost>();
        foreach (var row in rows.EnumerateArray())
        {
            if (ReadString(row, "group") != RaidGroup)
            {
                continue;
            }

            var toolStr = ReadString(row, "toolId");
            if (toolStr is null ||
                !int.TryParse(toolStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var toolId))
            {
                continue;
            }

            if (ReadDouble(row, "quantity") is not { } quantity)
            {
                continue;
            }

            costs.Add(new RaidCost(
                toolId,
                ReadString(row, "which"),
                ReadString(row, "caption"),
                quantity,
                ReadDouble(row, "time"),
                ReadInt(row, "sulfur"),
                ReadInt(row, "fuel")));
        }

        return costs;
    }

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? ReadInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

    private static double? ReadDouble(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
}
