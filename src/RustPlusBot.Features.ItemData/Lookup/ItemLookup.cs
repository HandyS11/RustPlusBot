using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>Pure name-or-id resolution. Exact name beats substring; ambiguous results are ranked and capped.</summary>
public static class ItemLookup
{
    private static readonly IComparer<ItemRecord> ById =
        Comparer<ItemRecord>.Create((a, b) => a.Id.CompareTo(b.Id));

    /// <summary>Resolves a user query to an item.</summary>
    /// <param name="query">The raw user input (name or id).</param>
    /// <param name="byId">Looks up an item by id.</param>
    /// <param name="all">All known items, for name matching.</param>
    /// <param name="cap">The maximum number of ambiguous candidates to return.</param>
    /// <returns>A <see cref="ItemMatch"/> describing the outcome.</returns>
    public static ItemMatch Resolve(string query,
        Func<int, ItemRecord?> byId,
        IReadOnlyList<ItemRecord> all,
        int cap = 10)
    {
        var (kind, single, candidates) = NameMatcher.Resolve(query, byId, all, i => i.Name, ById, cap);
        return kind switch
        {
            NameMatchKind.Found => new ItemMatch.Found(single!),
            NameMatchKind.Ambiguous => new ItemMatch.Ambiguous(candidates),
            _ => new ItemMatch.NotFound(),
        };
    }
}
