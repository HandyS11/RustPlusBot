using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>Pure name-or-id resolution over raid targets. Exact name beats substring.</summary>
public static class RaidLookup
{
    private static readonly IComparer<RaidTarget> ByName =
        Comparer<RaidTarget>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name));

    /// <summary>Resolves a user query to a raid target.</summary>
    /// <param name="query">The raw user input (target name or item id).</param>
    /// <param name="byId">Looks up an item-kind target by id.</param>
    /// <param name="all">All raid targets, for name matching.</param>
    /// <param name="cap">The maximum number of ambiguous candidates to return.</param>
    /// <returns>A <see cref="RaidMatch"/> describing the outcome.</returns>
    public static RaidMatch Resolve(string query,
        Func<int, RaidTarget?> byId,
        IReadOnlyList<RaidTarget> all,
        int cap = 10)
    {
        var (kind, single, candidates) = NameMatcher.Resolve(query, byId, all, t => t.Name, ByName, cap);
        return kind switch
        {
            NameMatchKind.Found => new RaidMatch.Found(single!),
            NameMatchKind.Ambiguous => new RaidMatch.Ambiguous(candidates),
            _ => new RaidMatch.NotFound(),
        };
    }
}
