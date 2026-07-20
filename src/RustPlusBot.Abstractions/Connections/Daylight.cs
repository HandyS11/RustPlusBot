using System.Globalization;

namespace RustPlusBot.Abstractions.Connections;

/// <summary>
///     Day/night reasoning over a <see cref="ServerTimeSnapshot" />. Shared by the in-game !time
///     handler and the #info server embed so the two can never disagree. All intervals are in
///     <em>in-game</em> hours: Rust's day length is server-configurable and the API never reports
///     it, so these values must not be presented as real-world minutes.
/// </summary>
public static class Daylight
{
    private const float HoursPerDay = 24f;

    /// <summary>True when the in-game clock sits between sunrise (inclusive) and sunset (exclusive).</summary>
    /// <param name="snapshot">The in-game time snapshot.</param>
    /// <returns>True during daylight; false at night.</returns>
    public static bool IsDay(ServerTimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.TimeOfDay >= snapshot.Sunrise && snapshot.TimeOfDay < snapshot.Sunset;
    }

    /// <summary>Renders the in-game clock as zero-padded "HH:mm".</summary>
    /// <param name="snapshot">The in-game time snapshot.</param>
    /// <returns>The clock text, e.g. "14:32".</returns>
    public static string Clock(ServerTimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var hours = (int)snapshot.TimeOfDay;
        var minutes = (int)((snapshot.TimeOfDay - hours) * 60f);
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}");
    }

    /// <summary>In-game hours until the next sunrise or sunset, wrapping past midnight.</summary>
    /// <param name="snapshot">The in-game time snapshot.</param>
    /// <returns>The interval in in-game hours; never negative.</returns>
    public static float HoursUntilTransition(ServerTimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var target = IsDay(snapshot) ? snapshot.Sunset : snapshot.Sunrise;
        var delta = target - snapshot.TimeOfDay;
        return delta >= 0f ? delta : delta + HoursPerDay;
    }

    /// <summary>The same interval as <see cref="HoursUntilTransition" />, as a <see cref="TimeSpan" />.</summary>
    /// <param name="snapshot">The in-game time snapshot.</param>
    /// <returns>The interval in in-game hours expressed as a TimeSpan.</returns>
    public static TimeSpan UntilTransition(ServerTimeSnapshot snapshot)
        => TimeSpan.FromHours(HoursUntilTransition(snapshot));
}
