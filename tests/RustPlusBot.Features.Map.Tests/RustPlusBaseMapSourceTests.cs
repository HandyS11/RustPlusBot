using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Composing;

namespace RustPlusBot.Features.Map.Tests;

public sealed class RustPlusBaseMapSourceTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();

    [Fact]
    public async Task GetAsync_returns_image_when_bytes_and_dimensions_present()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns((byte[])[1, 2, 3]);
        query.GetMapDimensionsAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(new MapDimensions(1000, 1000, 50, 2000));
        var source = new RustPlusBaseMapSource(query);

        var result = await source.GetAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal((byte[])[1, 2, 3], result.Bytes);
        Assert.Equal(1000, result.PixelWidth);
        Assert.Equal(1000, result.PixelHeight);
        Assert.Equal(50, result.OceanMarginPx);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_image_bytes_missing()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        query.GetMapDimensionsAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(new MapDimensions(1000, 1000, 50, 2000));
        var source = new RustPlusBaseMapSource(query);

        var result = await source.GetAsync(Guild, Server, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_dimensions_missing()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns((byte[])[1, 2, 3]);
        query.GetMapDimensionsAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns((MapDimensions?)null);
        var source = new RustPlusBaseMapSource(query);

        var result = await source.GetAsync(Guild, Server, CancellationToken.None);

        Assert.Null(result);
    }
}
