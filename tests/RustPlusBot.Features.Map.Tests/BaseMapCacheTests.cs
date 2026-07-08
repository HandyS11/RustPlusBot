using RustPlusBot.Features.Map.Composing;

namespace RustPlusBot.Features.Map.Tests;

public sealed class BaseMapCacheTests
{
    [Fact]
    public async Task First_source_wins_and_is_cached()
    {
        var preferred = new FakeSource(new BaseMapImage([1, 2], 100, 100, 0));
        var fallback = new FakeSource(new BaseMapImage([9, 9], 200, 200, 50));
        var cache = new BaseMapCache([preferred, fallback]);
        var server = Guid.NewGuid();

        var first = await cache.GetAsync(1, server, CancellationToken.None);
        var second = await cache.GetAsync(1, server, CancellationToken.None);

        Assert.Equal(100, first!.PixelWidth);
        Assert.Same(first, second);
        Assert.Equal(1, preferred.Calls); // cached after the first hit
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task Falls_through_to_next_source_when_first_returns_null()
    {
        var preferred = new FakeSource(null);
        var fallback = new FakeSource(new BaseMapImage([9], 200, 200, 50));
        var cache = new BaseMapCache([preferred, fallback]);

        var result = await cache.GetAsync(1, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(200, result!.PixelWidth);
    }

    [Fact]
    public async Task All_null_is_not_cached_and_retries()
    {
        var source = new FakeSource(null);
        var cache = new BaseMapCache([source]);
        var server = Guid.NewGuid();

        Assert.Null(await cache.GetAsync(1, server, CancellationToken.None));
        Assert.Null(await cache.GetAsync(1, server, CancellationToken.None));
        Assert.Equal(2, source.Calls);
    }

    private sealed class FakeSource(BaseMapImage? result) : IBaseMapSource
    {
        public int Calls { get; private set; }

        public Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
