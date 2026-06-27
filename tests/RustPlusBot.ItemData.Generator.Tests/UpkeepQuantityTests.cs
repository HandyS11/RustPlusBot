using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class UpkeepQuantityTests
{
    [Fact]
    public void Single_value_parses_to_equal_min_max()
    {
        var (min, max) = UpkeepQuantity.Parse("1");
        Assert.Equal(1, min);
        Assert.Equal(1, max);
    }

    [Fact]
    public void EnDash_range_parses_min_and_max()
    {
        var (min, max) = UpkeepQuantity.Parse("8–25"); // 8–25
        Assert.Equal(8, min);
        Assert.Equal(25, max);
    }

    [Fact]
    public void Hyphen_range_parses_min_and_max()
    {
        var (min, max) = UpkeepQuantity.Parse("20-67");
        Assert.Equal(20, min);
        Assert.Equal(67, max);
    }

    [Fact]
    public void Whitespace_is_trimmed()
    {
        var (min, max) = UpkeepQuantity.Parse("  5–17 ");
        Assert.Equal(5, min);
        Assert.Equal(17, max);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lots")]
    [InlineData("8–")]
    [InlineData("a–b")]
    public void Unparseable_throws(string raw)
    {
        Assert.Throws<FormatException>(() => UpkeepQuantity.Parse(raw));
    }
}
