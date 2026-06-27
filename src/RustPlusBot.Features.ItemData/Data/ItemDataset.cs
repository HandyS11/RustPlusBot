namespace RustPlusBot.Features.ItemData.Data;

/// <summary>The full bundled item dataset. Deserialized from the embedded <c>item-data.json</c>.</summary>
/// <param name="SchemaVersion">The schema version; the loader rejects a mismatched bundle.</param>
/// <param name="Sources">Per-section provenance dates.</param>
/// <param name="Items">Every known item, one record each.</param>
public sealed record ItemDataset(int SchemaVersion, DatasetSources Sources, IReadOnlyList<ItemRecord> Items);

/// <summary>When each section of the dataset was last sourced, for "data as of" display.</summary>
/// <param name="NamesAsOf">Names/ids/stack source date.</param>
/// <param name="RecycleAsOf">Recycle data source date.</param>
/// <param name="CraftAsOf">Craft data source date.</param>
/// <param name="ResearchAsOf">Research data source date.</param>
/// <param name="DecayAsOf">Decay data source date.</param>
/// <param name="UpkeepAsOf">Upkeep data source date.</param>
public sealed record DatasetSources(
    DateOnly NamesAsOf,
    DateOnly RecycleAsOf,
    DateOnly CraftAsOf,
    DateOnly ResearchAsOf,
    DateOnly DecayAsOf,
    DateOnly UpkeepAsOf);

/// <summary>One item, with all calculator data inlined (null where not applicable).</summary>
/// <param name="Id">The Rust item id.</param>
/// <param name="Name">The display name.</param>
/// <param name="StackSize">Max stack size.</param>
/// <param name="DespawnSeconds">Despawn time in seconds, or null if it does not despawn / unknown.</param>
/// <param name="Recycle">Recycler yield, or null if not recyclable.</param>
/// <param name="Craft">Craft recipe, or null if not craftable.</param>
/// <param name="Research">Research cost, or null if not researchable.</param>
/// <param name="Decay">Decay timing, or null if the item does not decay / unknown.</param>
/// <param name="Upkeep">Upkeep cost, or null if the item has no upkeep.</param>
public sealed record ItemRecord(
    int Id,
    string Name,
    int StackSize,
    int? DespawnSeconds,
    RecycleYield? Recycle,
    CraftRecipe? Craft,
    ResearchCost? Research,
    DecayInfo? Decay,
    UpkeepCost? Upkeep);

/// <summary>Recycler output for an item (6a covers the standard recycler only).</summary>
/// <param name="Recycler">The yield entries produced by the standard recycler.</param>
public sealed record RecycleYield(IReadOnlyList<YieldEntry> Recycler);

/// <summary>One recycler output entry.</summary>
/// <param name="ItemId">The produced item id.</param>
/// <param name="Quantity">The produced quantity.</param>
/// <param name="Probability">The probability (0..1) of receiving this output.</param>
public sealed record YieldEntry(int ItemId, int Quantity, double Probability);

/// <summary>A craft recipe.</summary>
/// <param name="Ingredients">The required ingredients.</param>
/// <param name="TimeSeconds">The craft time in seconds.</param>
/// <param name="WorkbenchLevel">Required workbench level (1-3), or null if none.</param>
public sealed record CraftRecipe(IReadOnlyList<Ingredient> Ingredients, double TimeSeconds, int? WorkbenchLevel);

/// <summary>One craft ingredient.</summary>
/// <param name="ItemId">The ingredient item id.</param>
/// <param name="Quantity">The required quantity.</param>
public sealed record Ingredient(int ItemId, int Quantity);

/// <summary>The research cost for an item.</summary>
/// <param name="Scrap">The scrap cost to research.</param>
public sealed record ResearchCost(int Scrap);

/// <summary>Decay timing for an item/deployable/building block. All fields nullable — a field is
/// populated only when RustLabs provides it. <paramref name="Seconds"/> is the base/default decay.</summary>
/// <param name="Seconds">Base decay time in seconds.</param>
/// <param name="OutsideSeconds">Decay time when placed outside, if distinct.</param>
/// <param name="InsideSeconds">Decay time when placed inside, if distinct.</param>
/// <param name="UnderwaterSeconds">Decay time when underwater, if distinct.</param>
/// <param name="Hp">The item's hit points.</param>
public sealed record DecayInfo(
    int? Seconds,
    int? OutsideSeconds,
    int? InsideSeconds,
    int? UnderwaterSeconds,
    int? Hp);

/// <summary>The upkeep cost to maintain a building block.</summary>
/// <param name="Entries">The per-resource upkeep cost entries.</param>
public sealed record UpkeepCost(IReadOnlyList<UpkeepEntry> Entries);

/// <summary>One upkeep resource cost. <paramref name="QuantityMin"/> equals
/// <paramref name="QuantityMax"/> for a single (non-range) quantity.</summary>
/// <param name="ItemId">The resource item id.</param>
/// <param name="QuantityMin">The lower bound of the cost.</param>
/// <param name="QuantityMax">The upper bound of the cost.</param>
public sealed record UpkeepEntry(int ItemId, int QuantityMin, int QuantityMax);
