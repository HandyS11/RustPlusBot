using System.Globalization;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>The kind of outcome produced by <see cref="NameMatcher"/>.</summary>
internal enum NameMatchKind
{
    /// <summary>No candidate matched.</summary>
    NotFound = 0,

    /// <summary>Exactly one candidate resolved.</summary>
    Found = 1,

    /// <summary>Several candidates matched; present for disambiguation.</summary>
    Ambiguous = 2,
}

/// <summary>Pure, generic name-or-id resolution shared by the item and raid-target lookups.
/// Exact name beats substring; an exact-name collision is ranked by <c>exactTieBreak</c>.</summary>
internal static class NameMatcher
{
    public static (NameMatchKind Kind, T? Single, IReadOnlyList<T> Candidates) Resolve<T>(
        string? query,
        Func<int, T?> byId,
        IReadOnlyList<T> all,
        Func<T, string> name,
        IComparer<T> exactTieBreak,
        int cap)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(byId);
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(exactTieBreak);

        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return (NameMatchKind.NotFound, null, []);
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && byId(id) is { } byIdHit)
        {
            return (NameMatchKind.Found, byIdHit, []);
        }

        var exact = all
            .Where(i => string.Equals(name(i), trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i, exactTieBreak)
            .ToList();
        if (exact.Count == 1)
        {
            return (NameMatchKind.Found, exact[0], []);
        }

        if (exact.Count > 1)
        {
            return (NameMatchKind.Ambiguous, null, [.. exact.Take(cap)]);
        }

        var matches = all
            .Where(i => name(i).Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => name(i).StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            .ThenBy(i => name(i).Length)
            .ThenBy(i => name(i), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count switch
        {
            0 => (NameMatchKind.NotFound, null, []),
            1 => (NameMatchKind.Found, matches[0], []),
            _ => (NameMatchKind.Ambiguous, null, [.. matches.Take(cap)]),
        };
    }
}
