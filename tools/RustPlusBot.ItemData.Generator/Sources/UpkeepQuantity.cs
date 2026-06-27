using System.Globalization;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Parses a RustLabs upkeep quantity string (e.g. <c>"1"</c> or <c>"8–25"</c>).</summary>
internal static class UpkeepQuantity
{
    /// <summary>Parses <paramref name="raw"/> into a (min, max) pair. A single value yields min == max.</summary>
    /// <param name="raw">The quantity string: a number or an en-dash/hyphen range.</param>
    /// <returns>The parsed lower and upper bounds.</returns>
    /// <exception cref="FormatException">The string is neither a number nor a number range.</exception>
    public static (int Min, int Max) Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var trimmed = raw.Trim();
        var dash = trimmed.IndexOfAny(['–', '-']);
        if (dash < 0)
        {
            var single = int.Parse(trimmed, CultureInfo.InvariantCulture);
            return (single, single);
        }

        var min = int.Parse(trimmed[..dash], CultureInfo.InvariantCulture);
        var max = int.Parse(trimmed[(dash + 1)..], CultureInfo.InvariantCulture);
        return (min, max);
    }
}
