using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Connections.Tests.Fakes;

namespace RustPlusBot.Features.Connections.Tests;

/// <summary>
/// Covers the per-connected-window map cache in isolation. Rust+ serves dimensions, monuments and the map
/// JPEG from one GetMap response (~683 KB), so the number of fetches this class issues is the whole point:
/// every extra one is a full map download.
/// </summary>
public sealed class ServerMapWindowCacheTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static FakeRustSocketSource.FakeConnection CreateConnection()
    {
        var source = new FakeRustSocketSource();
        source.Create("1.1.1.1", 28015, 555UL, "token");
        var connection = source.LastConnection!;
        connection.MonumentsResult = [new MonumentSnapshot("large_oil_rig", 1f, 2f)];
        connection.MapImageResult = [1, 2, 3];
        return connection;
    }

    private static ServerMapWindowCache CreateCache(FakeRustSocketSource.FakeConnection connection) =>
        new(connection, Timeout);

    [Fact]
    public async Task Resolves_once_and_serves_repeat_reads_from_memory()
    {
        var connection = CreateConnection();
        var cache = CreateCache(connection);

        for (var i = 0; i < 5; i++)
        {
            Assert.NotNull(await cache.GetDimensionsAsync(CancellationToken.None));
            Assert.NotEmpty(await cache.GetMonumentsAsync(CancellationToken.None));
            Assert.NotNull(await cache.GetImageAsync(CancellationToken.None));
        }

        Assert.Equal(1, connection.MapFetchCount);
    }

    [Fact]
    public async Task Concurrent_readers_collapse_to_a_single_fetch()
    {
        var connection = CreateConnection();
        var cache = CreateCache(connection);

        var readers = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => cache.GetMonumentsAsync(CancellationToken.None)))
            .ToArray();
        var results = await Task.WhenAll(readers);

        Assert.All(results, r => Assert.NotEmpty(r));
        Assert.Equal(1, connection.MapFetchCount);
    }

    [Fact]
    public async Task Does_not_cache_a_failed_fetch()
    {
        var connection = CreateConnection();
        connection.MapFault = new InvalidOperationException("GetMap returned no data.");
        var cache = CreateCache(connection);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetMonumentsAsync(CancellationToken.None));

        connection.MapFault = null;
        Assert.NotEmpty(await cache.GetMonumentsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancelling_one_reader_leaves_the_window_resolvable()
    {
        var connection = CreateConnection();
        var cache = CreateCache(connection);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetMonumentsAsync(cancelled.Token));

        // The single-flight gate is taken outside the try/finally, so a reader that never acquires it must
        // not leave the window wedged for everyone else.
        Assert.NotEmpty(await cache.GetMonumentsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DimensionsOrNull_peeks_without_fetching()
    {
        var connection = CreateConnection();
        var cache = CreateCache(connection);

        Assert.Null(cache.DimensionsOrNull);
        Assert.Equal(0, connection.MapFetchCount);

        await cache.GetDimensionsAsync(CancellationToken.None);

        Assert.NotNull(cache.DimensionsOrNull);
    }

    [Fact]
    public async Task Retries_the_world_size_without_re_downloading_the_map()
    {
        var connection = CreateConnection();
        connection.World = null; // GetInfo answered with an error — a rate limit on the connect burst, say.
        var cache = CreateCache(connection);

        Assert.Null(await cache.GetDimensionsAsync(CancellationToken.None));

        // The world size comes from GetInfo, which carries no JPEG, so a transient failure there must not
        // latch "no dimensions" for the window — that would leave #map dark until the next reconnect — and
        // must not cost a second full map download to recover from either.
        connection.World = new WorldSnapshot(4000u, 1u);
        Assert.NotNull(await cache.GetDimensionsAsync(CancellationToken.None));
        Assert.Equal(1, connection.MapFetchCount);
    }

    [Fact]
    public async Task Serves_monuments_and_the_image_even_when_the_world_size_is_unavailable()
    {
        var connection = CreateConnection();
        connection.World = null;
        var cache = CreateCache(connection);

        // A degraded GetInfo costs grid references, nothing else: the map response itself was fine.
        Assert.NotEmpty(await cache.GetMonumentsAsync(CancellationToken.None));
        Assert.NotNull(await cache.GetImageAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetImageAsync_returns_the_same_bytes_on_every_read()
    {
        var connection = CreateConnection();
        var cache = CreateCache(connection);

        var first = await cache.GetImageAsync(CancellationToken.None);

        // The query seam this backs is documented as an ordinary read. Handing the JPEG out once and null
        // afterwards would make #map depend on the base-map cache never being evicted mid-window.
        Assert.Same(first, await cache.GetImageAsync(CancellationToken.None));
        Assert.Equal(1, connection.MapFetchCount);
    }
}
