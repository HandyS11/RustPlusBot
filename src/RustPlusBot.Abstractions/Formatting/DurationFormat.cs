using System.Globalization;

namespace RustPlusBot.Abstractions.Formatting;

/// <summary>Formats durations compactly for in-game replies and Discord embeds.</summary>
public static class DurationFormat
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

    /// <summary>Renders a sub-minute-aware duration: "&lt;n&gt;s" under a minute, else "&lt;m&gt;m &lt;s&gt;s".</summary>
    /// <param name="seconds">The duration in seconds.</param>
    /// <returns>A compact duration string.</returns>
    public static string Seconds(double seconds)
    {
        if (seconds < 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{seconds:0.#}s");
        }

        var span = TimeSpan.FromSeconds(seconds);
        var minutes = (int)span.TotalMinutes;
        return span.Seconds == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes}m")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}m {span.Seconds}s");
    }
}
