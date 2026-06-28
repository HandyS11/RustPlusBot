using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Provides raid-target durability data (explosive costs) from RustLabs.</summary>
internal interface IDurabilitySource
{
    /// <summary>Loads raid targets, resolving item-kind target names via <paramref name="names"/>.</summary>
    /// <param name="names">Item id → display name, for the item-kind section.</param>
    /// <returns>Every target with at least one explosive cost.</returns>
    IReadOnlyList<RaidTarget> LoadRaidTargets(IReadOnlyDictionary<int, string> names);
}
