using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustMapsApi.Results;
using RustMapsApi.V4;
using RustMapsApi.V4.Models;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Composing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RustPlusBot.Features.Map.Tests;

public sealed class RustMapsBaseMapSourceTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();

    private static byte[] Png(int size)
    {
        using var img = new Image<Rgba32>(size, size);
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static IRustServerQuery QueryWithWorld(WorldSnapshot? world)
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetWorldAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(world);
        return query;
    }

    [Fact]
    public async Task Happy_path_downloads_raw_image_and_measures_dims()
    {
        var client = Substitute.For<IRustMapsClient>();
        client.GetMapBySeedAndSizeAsync(3500, 1234, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Success(new MapInfo
            {
                RawImageUrl = "https://img.example/raw.png"
            }, 200));
        var source = new RustMapsBaseMapSource(client, QueryWithWorld(new WorldSnapshot(3500, 1234)),
            new StubFactory(new StubHandler(HttpStatusCode.OK, Png(1750))), NullLogger<RustMapsBaseMapSource>.Instance);

        var result = await source.GetAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1750, result!.PixelWidth);
        Assert.Equal(0, result.OceanMarginPx);
    }

    [Fact]
    public async Task Map_not_found_returns_null()
    {
        var client = Substitute.For<IRustMapsClient>();
        client.GetMapBySeedAndSizeAsync(3500, 1234, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Failure(
                new RustMapsError(RustMapsErrorKind.NotFound, "not generated", RawBody: null, RetryAfter: null), 404));
        var source = new RustMapsBaseMapSource(client, QueryWithWorld(new WorldSnapshot(3500, 1234)),
            new StubFactory(new StubHandler(HttpStatusCode.OK, Png(16))), NullLogger<RustMapsBaseMapSource>.Instance);

        Assert.Null(await source.GetAsync(Guild, Server, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_world_info_returns_null()
    {
        var client = Substitute.For<IRustMapsClient>();
        var source = new RustMapsBaseMapSource(client, QueryWithWorld(null),
            new StubFactory(new StubHandler(HttpStatusCode.OK, Png(16))), NullLogger<RustMapsBaseMapSource>.Instance);

        Assert.Null(await source.GetAsync(Guild, Server, CancellationToken.None));
        await client.DidNotReceiveWithAnyArgs().GetMapBySeedAndSizeAsync(0, 0, false, CancellationToken.None);
    }

    private sealed class StubHandler(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
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
