using Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustMapsApi.V4;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Hosting;
using RustPlusBot.Features.Map.Posting;
using RustPlusBot.Features.Map.Rendering;
using RustPlusBot.Features.Map.RustMaps;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RustPlusBot.Features.Map.Tests.Hosting;

public sealed class InfoMapServiceTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly RustMapsMapKey Key = new(4000, 12345);

    private static byte[] BaseJpeg()
    {
        using var img = new Image<Rgba32>(64, 64, new Rgba32(0, 128, 0));
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static IServiceScopeFactory ScopeFactory(IWorkspaceStore workspaceStore) =>
        new ServiceCollection()
            .AddScoped(_ => workspaceStore)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private static (InfoMapHostedService Service, RustMapsMapCoordinator Coordinator, IInfoMapPoster Poster,
        IRustServerQuery Query) Build(ulong? channelId = 123UL, IInfoMapPoster? poster = null)
    {
        var coordinator = new RustMapsMapCoordinator();

        var query = Substitute.For<IRustServerQuery>();
        query.GetWorldAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(new WorldSnapshot((uint)Key.Size, (uint)Key.Seed));
        query.GetMapDimensionsAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(new MapDimensions(2000, 2000, 100, 4000));
        query.GetMonumentsAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns([]);

        var locator = Substitute.For<IInfoChannelLocator>();
        locator.GetChannelIdAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(channelId);

        poster ??= Substitute.For<IInfoMapPoster>();

        var workspaceStore = Substitute.For<IWorkspaceStore>();
        workspaceStore.GetCultureAsync(Guild, Arg.Any<CancellationToken>()).Returns("en");
        var scopeFactory = ScopeFactory(workspaceStore);

        var baseSource = new FakeBaseMapSource(new BaseMapImage(BaseJpeg(), 2000, 2000, 100));
        var composer = new MapComposer(
            new BaseMapCache([baseSource]),
            Substitute.For<IEventState>(),
            Substitute.For<IRigState>(),
            query,
            new MapRenderer(),
            scopeFactory);

        var driver = new RustMapsGenerationDriver(
            Substitute.For<IRustMapsClient>(),
            coordinator,
            Substitute.For<IHttpClientFactory>(),
            NullLogger<RustMapsGenerationDriver>.Instance);

        var service = new InfoMapHostedService(
            Substitute.For<IEventBus>(),
            coordinator,
            driver,
            composer,
            poster,
            locator,
            query,
            new ResxLocalizer(),
            Options.Create(new MapOptions()),
            scopeFactory,
            NullLogger<InfoMapHostedService>.Instance);

        return (service, coordinator, poster, query);
    }

    [Fact]
    public async Task Ready_posts_the_RustMaps_bytes_once_and_a_repeat_call_is_a_no_op()
    {
        var (service, coordinator, poster, _) = Build();
        byte[] readyBytes = [9, 9, 9];
        coordinator.SetReady(Key, new RustMapsReadyMap(readyBytes, "https://rustmaps/x"));

        await service.EnsureInfoMapAsync(Guild, Server, CancellationToken.None);
        await service.EnsureInfoMapAsync(Guild, Server, CancellationToken.None);

        await poster.Received(1).UpsertAsync(123UL, Arg.Any<ulong?>(), Arg.Any<Embed>(),
            Arg.Is<byte[]>(b => b.SequenceEqual(readyBytes)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Not_ready_posts_the_fallback_bytes_once_and_a_repeat_call_is_a_no_op()
    {
        var (service, coordinator, poster, _) = Build();

        await service.EnsureInfoMapAsync(Guild, Server, CancellationToken.None);
        await service.EnsureInfoMapAsync(Guild, Server, CancellationToken.None);

        // Idle for the whole test — no generation was ever advanced.
        Assert.Equal(RustMapsGenerationState.Idle, coordinator.Snapshot(Key).State);
        await poster.Received(1)
            .UpsertAsync(123UL, Arg.Any<ulong?>(), Arg.Any<Embed>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_info_channel_never_posts()
    {
        var (service, _, poster, _) = Build(channelId: null);

        await service.EnsureInfoMapAsync(Guild, Server, CancellationToken.None);

        await poster.DidNotReceiveWithAnyArgs().UpsertAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task No_info_channel_never_registers_the_key_with_the_coordinator()
    {
        var (service, coordinator, poster, _) = Build(channelId: null);

        await service.EnsureInfoMapAsync(Guild, Server, CancellationToken.None);

        Assert.Empty(coordinator.PendingKeys());
        Assert.Empty(coordinator.Requesters(Key));
        await poster.DidNotReceiveWithAnyArgs().UpsertAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task World_unavailable_never_posts()
    {
        var (service, _, poster, query) = Build();
        query.GetWorldAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns((WorldSnapshot?)null);

        await service.EnsureInfoMapAsync(Guild, Server, CancellationToken.None);

        await poster.DidNotReceiveWithAnyArgs().UpsertAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task Concurrent_calls_for_the_same_server_are_serialized_and_post_once()
    {
        // The connection-status loop and the tick loop both call EnsureInfoMapAsync for the same
        // server. Without per-server serialization their delete-then-repost interleaves and leaves
        // TWO #info messages (the fallback and the RustMaps render each survive the other's delete
        // scan). Serialized, the second caller sees the first's posted state and is a no-op.
        var poster = new GatedPoster();
        var (service, coordinator, _, _) = Build(poster: poster);
        coordinator.SetReady(Key, new RustMapsReadyMap([9, 9, 9], "https://rustmaps/x"));

        var first = service.EnsureInfoMapAsync(Guild, Server, CancellationToken.None);
        var second = service.EnsureInfoMapAsync(Guild, Server, CancellationToken.None);

        await poster.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); // first call is inside UpsertAsync
        await Task.Delay(100); // give the second call ample time to (try to) enter UpsertAsync
        Assert.Equal(1, poster.Calls); // serialized: the second call is blocked, not inside UpsertAsync

        poster.Release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, poster.Calls); // the second call saw the Ready state already posted → no-op
    }

    private sealed class GatedPoster : IInfoMapPoster
    {
        private int _calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls => Volatile.Read(ref _calls);

        public async Task<ulong?> UpsertAsync(ulong channelId,
            ulong? existingMessageId,
            Embed embed,
            byte[] pngBytes,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Entered.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            return 555UL;
        }
    }

    private sealed class FakeBaseMapSource(BaseMapImage image) : IBaseMapSource
    {
        public Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken) =>
            Task.FromResult<BaseMapImage?>(image);
    }
}
