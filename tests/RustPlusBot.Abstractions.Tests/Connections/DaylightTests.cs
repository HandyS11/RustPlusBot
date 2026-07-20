using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Tests.Connections;

public sealed class DaylightTests
{
    [Theory]
    [InlineData(12.0f, true)] // midday, between sunrise and sunset
    [InlineData(7.5f, true)] // just after sunrise
    [InlineData(2.0f, false)] // pre-dawn
    [InlineData(21.0f, false)] // after sunset
    [InlineData(7.0f, true)] // exactly sunrise counts as day
    [InlineData(20.0f, false)] // exactly sunset counts as night
    public void IsDay_bins_against_sunrise_and_sunset(float timeOfDay, bool expected)
    {
        var snapshot = new ServerTimeSnapshot(timeOfDay, 7.0f, 20.0f);

        Assert.Equal(expected, Daylight.IsDay(snapshot));
    }

    [Theory]
    [InlineData(14.53f, "14:31")]
    [InlineData(0.0f, "00:00")]
    [InlineData(9.5f, "09:30")]
    [InlineData(23.99f, "23:59")]
    public void Clock_formats_as_zero_padded_hours_and_minutes(float timeOfDay, string expected)
    {
        var snapshot = new ServerTimeSnapshot(timeOfDay, 7.0f, 20.0f);

        Assert.Equal(expected, Daylight.Clock(snapshot));
    }

    [Fact]
    public void HoursUntilTransition_during_day_counts_to_sunset()
    {
        var snapshot = new ServerTimeSnapshot(14.0f, 7.0f, 20.0f);

        Assert.Equal(6.0f, Daylight.HoursUntilTransition(snapshot), 3);
    }

    [Fact]
    public void HoursUntilTransition_before_sunrise_counts_to_sunrise()
    {
        var snapshot = new ServerTimeSnapshot(2.0f, 7.0f, 20.0f);

        Assert.Equal(5.0f, Daylight.HoursUntilTransition(snapshot), 3);
    }

    [Fact]
    public void HoursUntilTransition_after_sunset_wraps_past_midnight_to_sunrise()
    {
        var snapshot = new ServerTimeSnapshot(22.0f, 7.0f, 20.0f);

        // 2h to midnight + 7h to sunrise.
        Assert.Equal(9.0f, Daylight.HoursUntilTransition(snapshot), 3);
    }

    [Fact]
    public void UntilTransition_matches_HoursUntilTransition()
    {
        var snapshot = new ServerTimeSnapshot(14.0f, 7.0f, 20.0f);

        Assert.Equal(TimeSpan.FromHours(6.0).TotalSeconds, Daylight.UntilTransition(snapshot).TotalSeconds, 1.0);
    }
}
