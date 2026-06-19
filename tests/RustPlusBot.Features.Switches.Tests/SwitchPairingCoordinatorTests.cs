using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Switches.Pairing;
using RustPlusBot.Features.Switches.Posting;
using RustPlusBot.Features.Switches.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Switches;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Switches.Tests;

public sealed class SwitchPairingCoordinatorTests
{
    private static Harness Create()
    {
        var store = Substitute.For<ISwitchStore>();
        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped(_ => workspace);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var locator = Substitute.For<ISwitchChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(777UL);

        var poster = Substitute.For<ISwitchChannelPoster>();
        poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
                Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>())
            .Returns(900UL);

        var renderer = new SwitchEmbedRenderer(new SwitchLocalizer(SwitchLocalizationCatalog.Default));
        var coordinator = new SwitchPairingCoordinator(scopeFactory, locator, poster, renderer);
        return new Harness(coordinator, store, poster, locator);
    }

    [Fact]
    public async Task Paired_new_switch_posts_prompt_with_default_name()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);

        await h.Coordinator.HandlePairedAsync(new SwitchPairedEvent(10UL, serverId, 42UL), CancellationToken.None);

        await h.Poster.Received(1).EnsureAsync(777UL, null, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        Assert.Equal("Switch 42", h.Coordinator.PendingName(10UL, serverId, 42UL));
    }

    [Fact]
    public async Task Paired_already_managed_switch_is_ignored()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(true);

        await h.Coordinator.HandlePairedAsync(new SwitchPairedEvent(10UL, serverId, 42UL), CancellationToken.None);

        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        Assert.Null(h.Coordinator.PendingName(10UL, serverId, 42UL));
    }

    [Fact]
    public async Task Accept_persists_switch_and_replaces_prompt()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);
        await h.Coordinator.HandlePairedAsync(new SwitchPairedEvent(10UL, serverId, 42UL), CancellationToken.None);
        h.Store.AddAsync(10UL, serverId, 42UL, "Switch 42", 5UL, Arg.Any<CancellationToken>())
            .Returns(new RustPlusBot.Domain.Switches.SmartSwitch
            {
                GuildId = 10UL, ServerId = serverId, EntityId = 42UL, Name = "Switch 42",
            });

        var ok = await h.Coordinator.TryAcceptAsync(10UL, serverId, 42UL, acceptingUserId: 5UL, CancellationToken.None);

        Assert.True(ok);
        await h.Store.Received(1).AddAsync(10UL, serverId, 42UL, "Switch 42", 5UL, Arg.Any<CancellationToken>());
        Assert.Null(h.Coordinator.PendingName(10UL, serverId, 42UL)); // pending cleared
    }

    [Fact]
    public async Task Accept_is_noop_when_already_persisted_by_race()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(true);

        var ok = await h.Coordinator.TryAcceptAsync(10UL, serverId, 42UL, 5UL, CancellationToken.None);

        Assert.False(ok);
        await h.Store.DidNotReceive().AddAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(),
            Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    private sealed record Harness(
        SwitchPairingCoordinator Coordinator,
        ISwitchStore Store,
        ISwitchChannelPoster Poster,
        ISwitchChannelLocator Locator);
}
