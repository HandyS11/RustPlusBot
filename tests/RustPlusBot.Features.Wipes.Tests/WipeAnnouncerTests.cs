using Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Announcing;
using RustPlusBot.Features.Wipes.Posting;
using RustPlusBot.Features.Wipes.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Wipes.Tests;

/// <summary>Unit tests for <see cref="WipeAnnouncer"/>.</summary>
public sealed class WipeAnnouncerTests
{
    private static readonly Guid ServerId = Guid.NewGuid();

    private static Harness Create(ulong? channelId = 777UL, bool ping = false, string culture = "en")
    {
        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(culture);
        workspace.GetPingEveryoneOnWipeAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(ping);

        var services = new ServiceCollection();
        services.AddScoped(_ => workspace);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var locator = Substitute.For<IEventChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(channelId);
        var poster = Substitute.For<IWipeChannelPoster>();

        var announcer = new WipeAnnouncer(
            scopeFactory,
            locator,
            new WipeEmbedRenderer(new ResxLocalizer()),
            poster,
            NullLogger<WipeAnnouncer>.Instance);
        return new Harness(announcer, locator, poster);
    }

    private static ServerWipedEvent Event() =>
        new(10UL, ServerId, null, new DateTimeOffset(2026, 7, 2, 18, 0, 0, TimeSpan.Zero), 999u, 4250u);

    [Fact]
    public async Task Missing_events_channel_skips_posting()
    {
        var h = Create(channelId: null);

        await h.Announcer.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Poster.DidNotReceiveWithAnyArgs().PostAsync(0UL, default, null!, default);
    }

    [Fact]
    public async Task Posts_embed_without_ping_by_default()
    {
        var h = Create();

        await h.Announcer.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Poster.Received(1).PostAsync(
            777UL,
            null,
            Arg.Is<Embed>(e => e.Title == "🧹 Server wiped"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Posts_everyone_content_when_guild_setting_enabled()
    {
        var h = Create(ping: true);

        await h.Announcer.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Poster.Received(1).PostAsync(
            777UL,
            "@everyone",
            Arg.Any<Embed>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Renders_with_guild_culture()
    {
        var h = Create(culture: "fr");

        await h.Announcer.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Poster.Received(1).PostAsync(
            777UL,
            null,
            Arg.Is<Embed>(e => e.Title == "🧹 Serveur wipé"),
            Arg.Any<CancellationToken>());
    }

    private sealed record Harness(WipeAnnouncer Announcer, IEventChannelLocator Locator, IWipeChannelPoster Poster);
}
