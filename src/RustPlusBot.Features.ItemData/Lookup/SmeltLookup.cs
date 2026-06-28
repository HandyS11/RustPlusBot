using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>Pure name-or-id resolution over smelters. Exact name beats substring.</summary>
public static class SmeltLookup
{
    private static readonly IComparer<Smelter> ByName =
        Comparer<Smelter>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name));

    /// <summary>Resolves a user query to a smelter.</summary>
    /// <param name="query">The raw user input (smelter name or item id).</param>
    /// <param name="byId">Looks up a smelter by item id.</param>
    /// <param name="all">All smelters, for name matching.</param>
    /// <param name="cap">The maximum number of ambiguous candidates to return.</param>
    /// <returns>A <see cref="SmeltMatch"/> describing the outcome.</returns>
    public static SmeltMatch Resolve(string query,
        Func<int, Smelter?> byId,
        IReadOnlyList<Smelter> all,
        int cap = 10)
    {
        var (kind, single, candidates) = NameMatcher.Resolve(query, byId, all, s => s.Name, ByName, cap);
        return kind switch
        {
            NameMatchKind.Found => new SmeltMatch.Found(single!),
            NameMatchKind.Ambiguous => new SmeltMatch.Ambiguous(candidates),
            _ => new SmeltMatch.NotFound(),
        };
    }
}
