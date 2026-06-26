using System.Globalization;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>Pure name-or-id resolution. Exact name beats substring; ambiguous results are ranked and capped.</summary>
public static class ItemLookup
{
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
        ArgumentNullException.ThrowIfNull(byId);
        ArgumentNullException.ThrowIfNull(all);

        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return new ItemMatch.NotFound();
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && byId(id) is { } byIdHit)
        {
            return new ItemMatch.Found(byIdHit);
        }

        var exactMatches = all
            .Where(i => string.Equals(i.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Id)
            .ToList();
        if (exactMatches.Count == 1)
        {
            return new ItemMatch.Found(exactMatches[0]);
        }

        if (exactMatches.Count > 1)
        {
            return new ItemMatch.Ambiguous([.. exactMatches.Take(cap)]);
        }

        var matches = all
            .Where(i => i.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            .ThenBy(i => i.Name.Length)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count switch
        {
            0 => new ItemMatch.NotFound(),
            1 => new ItemMatch.Found(matches[0]),
            _ => new ItemMatch.Ambiguous([.. matches.Take(cap)]),
        };
    }
}
