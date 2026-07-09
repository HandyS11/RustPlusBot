using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Map;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Workspace.Tests.Messages;

public sealed class ServerInfoMapMessageRendererTests
{
    private static readonly ResxLocalizer Loc = new();

    private static IRustServerQuery QueryWithWorld(ulong guildId, Guid serverId, uint size = 4000, uint seed = 12345)
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetWorldAsync(guildId, serverId, Arg.Any<CancellationToken>())
            .Returns(new WorldSnapshot(size, seed));
        return query;
    }

    [Fact]
    public async Task NullServerId_RendersEmptyPayload()
    {
        var query = Substitute.For<IRustServerQuery>();
        var renderer = new ServerInfoMapMessageRenderer(query, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, null, "en"), default);

        Assert.Null(payload.Text);
        Assert.Null(payload.Embed);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Ready_view_sets_the_embed_image_and_url()
    {
        var serverId = Guid.NewGuid();
        var query = QueryWithWorld(1, serverId);
        var readModel = Substitute.For<IInfoMapReadModel>();
        readModel.GetReady(4000, 12345).Returns(new InfoMapView("https://img/icons.png", "https://rustmaps/x"));
        var renderer = new ServerInfoMapMessageRenderer(query, Loc, readModel);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.NotNull(payload.Embed);
        Assert.Equal("https://img/icons.png", payload.Embed!.Image!.Value.Url);
        Assert.Equal("https://rustmaps/x", payload.Embed.Url);
    }

    [Fact]
    public async Task Ready_view_without_page_url_omits_the_embed_url()
    {
        var serverId = Guid.NewGuid();
        var query = QueryWithWorld(1, serverId);
        var readModel = Substitute.For<IInfoMapReadModel>();
        readModel.GetReady(4000, 12345).Returns(new InfoMapView("https://img/icons.png", null));
        var renderer = new ServerInfoMapMessageRenderer(query, Loc, readModel);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Equal("https://img/icons.png", payload.Embed!.Image!.Value.Url);
        Assert.Null(payload.Embed.Url);
    }

    [Fact]
    public async Task No_readModel_configured_still_returns_a_non_empty_embed_with_the_generating_footer()
    {
        // IInfoMapReadModel is only registered when a RustMaps API key is configured — the renderer must
        // still be usable (and non-empty, so the reconciler doesn't skip it) with the optional dependency unset.
        var serverId = Guid.NewGuid();
        var query = QueryWithWorld(1, serverId);
        var renderer = new ServerInfoMapMessageRenderer(query, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.NotNull(payload.Embed);
        Assert.Null(payload.Embed!.Image);
        Assert.Equal(Loc.Get("map.info.generating", "en"), payload.Embed.Footer!.Value.Text);
    }

    [Fact]
    public async Task Not_ready_yet_shows_the_generating_footer()
    {
        var serverId = Guid.NewGuid();
        var query = QueryWithWorld(1, serverId);
        var readModel = Substitute.For<IInfoMapReadModel>();
        readModel.GetReady(4000, 12345).Returns((InfoMapView?)null);
        var renderer = new ServerInfoMapMessageRenderer(query, Loc, readModel);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.NotNull(payload.Embed);
        Assert.Equal(Loc.Get("map.info.generating", "en"), payload.Embed!.Footer!.Value.Text);
    }

    [Fact]
    public async Task No_world_yet_still_returns_a_non_empty_embed_so_the_reconciler_does_not_skip_it()
    {
        // Ordering caveat: the reconciler skips an empty payload, which would let ServerInfo claim the
        // top slot. This message must be non-empty even before the world snapshot is available.
        var serverId = Guid.NewGuid();
        var query = Substitute.For<IRustServerQuery>();
        query.GetWorldAsync(1, serverId, Arg.Any<CancellationToken>()).Returns((WorldSnapshot?)null);
        var renderer = new ServerInfoMapMessageRenderer(query, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.NotNull(payload.Embed);
        Assert.Empty(payload.Embed!.Fields);
    }

    [Fact]
    public async Task World_present_shows_size_and_seed_fields()
    {
        var serverId = Guid.NewGuid();
        var query = QueryWithWorld(1, serverId);
        var renderer = new ServerInfoMapMessageRenderer(query, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var body = string.Concat(payload.Embed!.Fields.Select(f => f.Value));
        Assert.Contains("4000", body, StringComparison.Ordinal);
        Assert.Contains("12345", body, StringComparison.Ordinal);
    }
}
