using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>Pure name resolution over CCTV monuments. Exact name beats substring.</summary>
public static class CctvLookup
{
    private static readonly IComparer<CctvMonument> ByName =
        Comparer<CctvMonument>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name));

    /// <summary>Resolves a user query to a CCTV monument.</summary>
    /// <param name="query">The raw user input (monument name).</param>
    /// <param name="all">All monuments, for name matching.</param>
    /// <param name="cap">The maximum number of ambiguous candidates to return.</param>
    /// <returns>A <see cref="CctvMatch"/> describing the outcome.</returns>
    public static CctvMatch Resolve(string query, IReadOnlyList<CctvMonument> all, int cap = 10)
    {
        var (kind, single, candidates) =
            NameMatcher.Resolve(query, static _ => (CctvMonument?)null, all, m => m.Name, ByName, cap);
        return kind switch
        {
            NameMatchKind.Found => new CctvMatch.Found(single!),
            NameMatchKind.Ambiguous => new CctvMatch.Ambiguous(candidates),
            _ => new CctvMatch.NotFound(),
        };
    }
}
