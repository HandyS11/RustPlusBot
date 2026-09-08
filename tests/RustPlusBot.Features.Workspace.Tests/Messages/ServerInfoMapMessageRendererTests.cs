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

    private static IInfoMapReadModel ReadModel(Guid serverId, InfoMapResolution resolution)
    {
        var readModel = Substitute.For<IInfoMapReadModel>();
        readModel.Resolve(1, serverId, 4000, 12345).Returns(resolution);
        return readModel;
    }

    private static InfoMapResolution Verified(string imageUrl, string? pageUrl) =>
        new(InfoMapStatus.Verified, new InfoMapView(imageUrl, pageUrl));

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
        var readModel = ReadModel(serverId, Verified("https://img/icons.png", "https://rustmaps/x"));
        var renderer = new ServerInfoMapMessageRenderer(query, Loc, readModel);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.NotNull(payload.Embed);
        Assert.Equal("https://img/icons.png", payload.Embed.Image!.Value.Url);
        Assert.Equal("https://rustmaps/x", payload.Embed.Url);
    }

    [Fact]
    public async Task Ready_view_without_page_url_omits_the_embed_url()
    {
        var serverId = Guid.NewGuid();
        var query = QueryWithWorld(1, serverId);
        var readModel = ReadModel(serverId, Verified("https://img/icons.png", null));
        var renderer = new ServerInfoMapMessageRenderer(query, Loc, readModel);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Equal("https://img/icons.png", payload.Embed!.Image!.Value.Url);
        Assert.Null(payload.Embed.Url);
    }

    [Fact]
    public async Task No_readModel_configured_renders_empty_payload()
    {
        // IInfoMapReadModel is only registered when a RustMaps API key is configured. With no render source
        // there is nothing to show, so the renderer returns an empty payload and the reconciler leaves the
        // channel untouched (rather than overwriting an existing map message with a placeholder).
        var serverId = Guid.NewGuid();
        var query = QueryWithWorld(1, serverId);
        var renderer = new ServerInfoMapMessageRenderer(query, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Null(payload.Text);
        Assert.Null(payload.Embed);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Not_ready_yet_renders_empty_payload()
    {
        // Until the RustMaps render is ready the renderer returns nothing, so the reconciler preserves any
        // existing map message (with its last image) instead of flickering to a placeholder on every boot.
        var serverId = Guid.NewGuid();
        var query = QueryWithWorld(1, serverId);
        var readModel = ReadModel(serverId, InfoMapResolution.Pending);
        var renderer = new ServerInfoMapMessageRenderer(query, Loc, readModel);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Null(payload.Text);
        Assert.Null(payload.Embed);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task No_world_yet_renders_empty_payload()
    {
        // No world snapshot means the map key is unknown and nothing can be ready — empty payload, no post.
        var serverId = Guid.NewGuid();
        var query = Substitute.For<IRustServerQuery>();
        query.GetWorldAsync(1, serverId, Arg.Any<CancellationToken>()).Returns((WorldSnapshot?)null);
        var renderer = new ServerInfoMapMessageRenderer(query, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Null(payload.Text);
        Assert.Null(payload.Embed);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Mismatched_render_is_replaced_by_the_server_own_map()
    {
        // The RustMaps render for a (size, seed) is not necessarily this server's world: a server running a
        // pre-generated/custom level reports the seed from its config, and the render is then a different
        // island. The image Rust+ serves is the only truthful one.
        var serverId = Guid.NewGuid();
        var query = QueryWithWorld(1, serverId);
        var image = new byte[]
        {
            1, 2, 3, 4
        };
        query.GetMapImageAsync(1, serverId, Arg.Any<CancellationToken>()).Returns(image);
        var renderer = new ServerInfoMapMessageRenderer(query, Loc,
            ReadModel(serverId, new InfoMapResolution(InfoMapStatus.Mismatched, null)));

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.NotNull(payload.Attachment);
        Assert.Same(image, payload.Attachment.Bytes);
        Assert.Equal($"attachment://{payload.Attachment.FileName}", payload.Embed!.Image!.Value.Url);
        Assert.DoesNotContain("rustmaps", payload.Embed.Image.Value.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mismatched_attachment_name_tracks_the_image_content()
    {
        // The reconciler tells "already posted" from "the map changed" by file name alone, so the name must
        // be derived from the bytes: same map → same name (no re-upload), new map → new name (repost).
        var serverId = Guid.NewGuid();
        var query = QueryWithWorld(1, serverId);
        var readModel = ReadModel(serverId, new InfoMapResolution(InfoMapStatus.Mismatched, null));
        var renderer = new ServerInfoMapMessageRenderer(query, Loc, readModel);

        query.GetMapImageAsync(1, serverId, Arg.Any<CancellationToken>()).Returns([1, 2, 3, 4]);
        var first = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);
        var again = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);
        query.GetMapImageAsync(1, serverId, Arg.Any<CancellationToken>()).Returns([9, 9, 9, 9]);
        var other = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Equal(first.Attachment!.FileName, again.Attachment!.FileName);
        Assert.NotEqual(first.Attachment.FileName, other.Attachment!.FileName);
    }

    [Fact]
    public async Task Mismatch_without_a_server_image_renders_empty_payload()
    {
        // No truthful image to show and the wrong one must not be posted: leave the channel untouched.
        var serverId = Guid.NewGuid();
        var query = QueryWithWorld(1, serverId);
        query.GetMapImageAsync(1, serverId, Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        var renderer = new ServerInfoMapMessageRenderer(query, Loc,
            ReadModel(serverId, new InfoMapResolution(InfoMapStatus.Mismatched, null)));

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Null(payload.Embed);
        Assert.Null(payload.Attachment);
    }

    [Fact]
    public async Task Ready_view_shows_size_and_seed_fields()
    {
        var serverId = Guid.NewGuid();
        var query = QueryWithWorld(1, serverId);
        var readModel = ReadModel(serverId, Verified("https://img/icons.png", null));
        var renderer = new ServerInfoMapMessageRenderer(query, Loc, readModel);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var body = string.Concat(payload.Embed!.Fields.Select(f => f.Value));
        Assert.Contains("4000", body, StringComparison.Ordinal);
        Assert.Contains("12345", body, StringComparison.Ordinal);
    }
}
