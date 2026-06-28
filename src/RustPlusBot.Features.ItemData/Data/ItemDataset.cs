namespace RustPlusBot.Features.ItemData.Data;

/// <summary>The full bundled item dataset. Deserialized from the embedded <c>item-data.json</c>.</summary>
/// <param name="SchemaVersion">The schema version; the loader rejects a mismatched bundle.</param>
/// <param name="Sources">Per-section provenance dates.</param>
/// <param name="Items">Every known item, one record each.</param>
/// <param name="RaidTargets">Every raid target (item/building-block/vehicle) and its explosive cost.</param>
public sealed record ItemDataset(
    int SchemaVersion,
    DatasetSources Sources,
    IReadOnlyList<ItemRecord> Items,
    IReadOnlyList<RaidTarget> RaidTargets);

/// <summary>When each section of the dataset was last sourced, for "data as of" display.</summary>
/// <param name="NamesAsOf">Names/ids/stack source date.</param>
/// <param name="RecycleAsOf">Recycle data source date.</param>
/// <param name="CraftAsOf">Craft data source date.</param>
/// <param name="ResearchAsOf">Research data source date.</param>
/// <param name="DecayAsOf">Decay data source date.</param>
/// <param name="UpkeepAsOf">Upkeep data source date.</param>
/// <param name="DurabilityAsOf">Durability/raid-cost data source date.</param>
public sealed record DatasetSources(
    DateOnly NamesAsOf,
    DateOnly RecycleAsOf,
    DateOnly CraftAsOf,
    DateOnly ResearchAsOf,
    DateOnly DecayAsOf,
    DateOnly UpkeepAsOf,
    DateOnly DurabilityAsOf);

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

/// <summary>The kind of raid target, mapping to the three sections of the RustLabs durability source.</summary>
public enum RaidTargetKind
{
    /// <summary>A deployable item (resolves to a known item id).</summary>
    Item = 0,

    /// <summary>A building block (wall, door, floor) — name-keyed, not an item.</summary>
    BuildingBlock = 1,

    /// <summary>A vehicle or NPC target — name-keyed, not an item.</summary>
    Vehicle = 2,
}

/// <summary>One raid target and the explosive cost to destroy it.</summary>
/// <param name="Key">The item id as a string (<see cref="RaidTargetKind.Item"/>) or the target name otherwise.</param>
/// <param name="Name">The display name.</param>
/// <param name="Kind">The target kind.</param>
/// <param name="Costs">The per-explosive cost entries (always non-empty).</param>
public sealed record RaidTarget(string Key, string Name, RaidTargetKind Kind, IReadOnlyList<RaidCost> Costs);

/// <summary>One explosive's cost against a target. Fields are null where RustLabs omits them.</summary>
/// <param name="ToolId">The explosive item id (resolves via the item spine).</param>
/// <param name="Side">The building-block face: "soft", "hard", "both", or null.</param>
/// <param name="Caption">A sub-label (e.g. ammo variant or placement note), or null.</param>
/// <param name="Quantity">Units of the tool required.</param>
/// <param name="TimeSeconds">Total time in seconds, or null.</param>
/// <param name="Sulfur">Total sulfur cost, or null.</param>
/// <param name="Fuel">Total low-grade fuel cost, or null.</param>
public sealed record RaidCost(
    int ToolId,
    string? Side,
    string? Caption,
    double Quantity,
    double? TimeSeconds,
    int? Sulfur,
    int? Fuel);
