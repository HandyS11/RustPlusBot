using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Provides per-smelter smelting/cooking conversion data from RustLabs.</summary>
internal interface ISmeltingSource
{
    /// <summary>Loads smelters, resolving smelter and input/output names via <paramref name="names"/>.</summary>
    /// <param name="names">Item id → display name, for the smelter id and each conversion's ids.</param>
    /// <returns>Every smelter with at least one resolvable conversion.</returns>
    IReadOnlyList<Smelter> LoadSmelters(IReadOnlyDictionary<int, string> names);
}
