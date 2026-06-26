using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Provides recycle, craft, and research data from RustLabs.</summary>
internal interface IRustLabsSource
{
    /// <summary>Loads recycle yields keyed by item id.</summary>
    IReadOnlyDictionary<int, RecycleYield> LoadRecycleYields();

    /// <summary>Loads craft recipes keyed by item id.</summary>
    IReadOnlyDictionary<int, CraftRecipe> LoadCraftRecipes();

    /// <summary>Loads research costs keyed by item id.</summary>
    IReadOnlyDictionary<int, ResearchCost> LoadResearchCosts();
}
