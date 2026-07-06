using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Composing;

namespace RustPlusBot.Features.Map.Tests;

public sealed class BaseMapCacheTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();

    [Fact]
    public async Task GetAsync_fetches_once_then_serves_from_cache()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(
            [
                9
            ]);
        var cache = new BaseMapCache(query);

        var first = await cache.GetAsync(Guild, Server, CancellationToken.None);
        var second = await cache.GetAsync(Guild, Server, CancellationToken.None);

        Assert.Equal("\t"u8.ToArray(), first);
        Assert.Equal("\t"u8.ToArray(), second);
        await query.Received(1).GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_does_not_cache_null_and_retries()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns((byte[]?)null,
            [
                7
            ]);
        var cache = new BaseMapCache(query);

        var first = await cache.GetAsync(Guild, Server, CancellationToken.None);
        var second = await cache.GetAsync(Guild, Server, CancellationToken.None);

        Assert.Null(first);
        Assert.Equal(new byte[]
        {
            7
        }, second);
        await query.Received(2).GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clear_evicts_so_next_get_refetches()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(
        [
            1
        ]);
        var cache = new BaseMapCache(query);

        await cache.GetAsync(Guild, Server, CancellationToken.None);
        cache.Clear(Guild, Server);
        await cache.GetAsync(Guild, Server, CancellationToken.None);

        await query.Received(2).GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>());
    }
}
