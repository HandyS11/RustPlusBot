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
public sealed record DatasetSources(
    DateOnly NamesAsOf,
    DateOnly RecycleAsOf,
    DateOnly CraftAsOf,
    DateOnly ResearchAsOf);

/// <summary>One item, with all 6a calculator data inlined (null where not applicable).</summary>
/// <param name="Id">The Rust item id.</param>
/// <param name="Name">The display name.</param>
/// <param name="StackSize">Max stack size.</param>
/// <param name="DespawnSeconds">Despawn time in seconds, or null if it does not despawn / unknown.</param>
/// <param name="Recycle">Recycler yield, or null if not recyclable.</param>
/// <param name="Craft">Craft recipe, or null if not craftable.</param>
/// <param name="Research">Research cost, or null if not researchable.</param>
public sealed record ItemRecord(
    int Id,
    string Name,
    int StackSize,
    int? DespawnSeconds,
    RecycleYield? Recycle,
    CraftRecipe? Craft,
    ResearchCost? Research);

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
