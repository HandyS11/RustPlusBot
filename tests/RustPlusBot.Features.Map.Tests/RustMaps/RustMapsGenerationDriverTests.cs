using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustMapsApi.Results;
using RustMapsApi.V4;
using RustMapsApi.V4.Models;
using RustMapsApi.V4.Requests;
using RustPlusBot.Features.Map.RustMaps;

namespace RustPlusBot.Features.Map.Tests.RustMaps;

public sealed class RustMapsGenerationDriverTests
{
    private static readonly RustMapsMapKey Key = new(4000, 12345);
    private static readonly Guid Server = Guid.NewGuid();

    private static RustMapsError Error(RustMapsErrorKind kind) => new(kind, null, null, null);

    private static (RustMapsGenerationDriver Driver, IRustMapsClient Client, RustMapsMapCoordinator Coord) Build(
        HttpStatusCode imageStatus = HttpStatusCode.OK)
    {
        var client = Substitute.For<IRustMapsClient>();
        var coord = new RustMapsMapCoordinator();
        var factory = new StubFactory(new StubHandler(imageStatus, [7, 7, 7]));
        var driver = new RustMapsGenerationDriver(client, coord, factory,
            NullLogger<RustMapsGenerationDriver>.Instance);
        return (driver, client, coord);
    }

    [Fact]
    public async Task Existing_map_goes_straight_to_ready_without_generating()
    {
        var (driver, client, coord) = Build();
        coord.Register(Key, 1UL, Server);
        client.GetMapBySeedAndSizeAsync(4000, 12345, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Success(
                new MapInfo
                {
                    ImageUrl = "https://img/x.png", Url = "https://rustmaps/x"
                }, 200));

        await driver.AdvanceAsync(Key, CancellationToken.None);

        Assert.Equal(RustMapsGenerationState.Ready, coord.Snapshot(Key).State);
        Assert.Equal([7, 7, 7], coord.Snapshot(Key).Ready!.ImageBytes);
        await client.DidNotReceiveWithAnyArgs().CreateMapAsync(default!, default);
    }

    [Fact]
    public async Task NotFound_with_budget_generates_exactly_once_across_ticks()
    {
        var (driver, client, coord) = Build();
        coord.Register(Key, 1UL, Server);
        client.GetMapBySeedAndSizeAsync(4000, 12345, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Failure(Error(RustMapsErrorKind.NotFound), 404));
        client.GetLimitsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<MapGenLimits>.Success(
                new MapGenLimits
                {
                    Concurrent = new(0, 5), Monthly = new(3, 100)
                }, 200));
        client.CreateMapAsync(Arg.Any<MapGenerationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<MapGenerationStatus>.Success(new MapGenerationStatus
            {
                MapId = "map-1"
            }, 200));

        await driver.AdvanceAsync(Key, CancellationToken.None); // Idle → Generating
        await driver.AdvanceAsync(Key, CancellationToken.None); // Generating → poll (still not ready)

        Assert.Equal(RustMapsGenerationState.Generating, coord.Snapshot(Key).State);
        await client.Received(1).CreateMapAsync(
            Arg.Is<MapGenerationRequest>(r => r.Size == 4000 && r.Seed == 12345 && !r.Staging),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Limit_reached_never_generates()
    {
        var (driver, client, coord) = Build();
        coord.Register(Key, 1UL, Server);
        client.GetMapBySeedAndSizeAsync(4000, 12345, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Failure(Error(RustMapsErrorKind.NotFound), 404));
        client.GetLimitsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<MapGenLimits>.Success(
                new MapGenLimits
                {
                    Concurrent = new(5, 5), Monthly = new(3, 100)
                }, 200)); // concurrent exhausted

        await driver.AdvanceAsync(Key, CancellationToken.None);

        Assert.Equal(RustMapsGenerationState.LimitReached, coord.Snapshot(Key).State);
        await client.DidNotReceiveWithAnyArgs().CreateMapAsync(default!, default);
    }

    [Fact]
    public async Task Limits_call_failing_fails_closed_no_generate()
    {
        var (driver, client, coord) = Build();
        coord.Register(Key, 1UL, Server);
        client.GetMapBySeedAndSizeAsync(4000, 12345, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Failure(Error(RustMapsErrorKind.NotFound), 404));
        client.GetLimitsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<MapGenLimits>.Failure(Error(RustMapsErrorKind.Transport), 503));

        await driver.AdvanceAsync(Key, CancellationToken.None);

        Assert.Equal(RustMapsGenerationState.Failed, coord.Snapshot(Key).State);
        await client.DidNotReceiveWithAnyArgs().CreateMapAsync(default!, default);
    }

    [Fact]
    public async Task Poll_reaches_ready_and_downloads_the_image()
    {
        var (driver, client, coord) = Build();
        coord.Register(Key, 1UL, Server);
        coord.TrySetGenerating(Key, "map-1");
        client.GetMapByIdAsync("map-1", Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Success(
                new MapInfo
                {
                    ImageUrl = "https://img/x.png", Url = "https://rustmaps/x"
                }, 200));

        await driver.AdvanceAsync(Key, CancellationToken.None);

        Assert.Equal(RustMapsGenerationState.Ready, coord.Snapshot(Key).State);
        Assert.Equal([7, 7, 7], coord.Snapshot(Key).Ready!.ImageBytes);
    }

    [Fact]
    public async Task Poll_still_generating_stays_generating_without_spending()
    {
        var (driver, client, coord) = Build();
        coord.Register(Key, 1UL, Server);
        coord.TrySetGenerating(Key, "map-1");
        client.GetMapByIdAsync("map-1", Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Failure(Error(RustMapsErrorKind.Queued), 409));

        await driver.AdvanceAsync(Key, CancellationToken.None);

        Assert.Equal(RustMapsGenerationState.Generating, coord.Snapshot(Key).State);
        await client.DidNotReceiveWithAnyArgs().CreateMapAsync(default!, default);
    }

    private sealed class StubHandler(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(body)
            });
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
