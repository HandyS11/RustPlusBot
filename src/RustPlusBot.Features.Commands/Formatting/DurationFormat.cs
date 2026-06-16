using System.Globalization;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats durations compactly for in-game replies.</summary>
internal static class DurationFormat
{
    /// <summary>Renders a duration as "Xd Yh" / "Yh Zm" / "Zm".</summary>
    /// <param name="span">The duration to render.</param>
    /// <returns>A compact duration string.</returns>
    public static string Compact(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays}d {span.Hours}h");
        }

        if (span.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes}m");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}m");
    }
}
