using RustPlusBot.Features.Commands.Formatting;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class DistanceTests
{
    [Fact]
    public void Between_ReturnsEuclideanDistance_Rounded()
    {
        Assert.Equal(5, Distance.Between(0f, 0f, 3f, 4f)); // 3-4-5 triangle
    }

    [Fact]
    public void Between_ReturnsZero_ForSamePoint()
    {
        Assert.Equal(0, Distance.Between(10f, 10f, 10f, 10f));
    }

    [Fact]
    public void Between_RoundsToNearestInteger()
    {
        Assert.Equal(1, Distance.Between(0f, 0f, 1f, 0f));
        Assert.Equal(141, Distance.Between(0f, 0f, 100f, 100f)); // 141.42 -> 141
    }
}
