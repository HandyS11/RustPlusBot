using RustPlusBot.Features.Commands.Formatting;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class DurationFormatTests
{
    [Theory]
    [InlineData(0, 0, 30, "30m")]
    [InlineData(0, 5, 0, "5h 0m")]
    [InlineData(2, 3, 0, "2d 3h")]
    public void Compact(int days, int hours, int minutes, string expected) =>
        Assert.Equal(expected, DurationFormat.Compact(new TimeSpan(days, hours, minutes, 0)));
}
