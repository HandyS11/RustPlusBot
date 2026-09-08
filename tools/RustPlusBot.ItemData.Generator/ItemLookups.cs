using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator;

/// <summary>The per-item, id-keyed lookups merged into each <see cref="ItemRecord"/>.</summary>
/// <param name="Names">Item id to display name.</param>
/// <param name="StackSizes">Item id to max stack size.</param>
/// <param name="DespawnSeconds">Item id to despawn time in seconds.</param>
/// <param name="RecycleYields">Item id to recycler yield.</param>
/// <param name="CraftRecipes">Item id to craft recipe.</param>
/// <param name="ResearchCosts">Item id to research cost.</param>
/// <param name="DecayInfos">Item id to decay timing.</param>
/// <param name="UpkeepCosts">Item id to upkeep cost.</param>
internal sealed record ItemLookups(
    IReadOnlyDictionary<int, string> Names,
    IReadOnlyDictionary<int, int> StackSizes,
    IReadOnlyDictionary<int, int> DespawnSeconds,
    IReadOnlyDictionary<int, RecycleYield> RecycleYields,
    IReadOnlyDictionary<int, CraftRecipe> CraftRecipes,
    IReadOnlyDictionary<int, ResearchCost> ResearchCosts,
    IReadOnlyDictionary<int, DecayInfo> DecayInfos,
    IReadOnlyDictionary<int, UpkeepCost> UpkeepCosts);
