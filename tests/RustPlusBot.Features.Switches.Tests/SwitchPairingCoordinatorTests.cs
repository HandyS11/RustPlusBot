using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Switches.Pairing;
using RustPlusBot.Features.Switches.Posting;
using RustPlusBot.Features.Switches.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
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

        var posted = new List<global::Discord.Embed>();
        var poster = Substitute.For<ISwitchChannelPoster>();
        poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Do<global::Discord.Embed>(posted.Add),
                Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>())
            .Returns(900UL);

        var renderer = new SwitchEmbedRenderer(new ResxLocalizer());
        var coordinator = new SwitchPairingCoordinator(scopeFactory, locator, poster, renderer);
        return new Harness(coordinator, store, poster, locator, posted);
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
    public async Task Paired_without_a_provisioned_channel_posts_nothing_and_holds_no_pending()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);
        h.Locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)null);

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
    public async Task Accept_edits_the_prompt_message_into_the_switch_embed_and_stores_the_message_id()
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

        var ok = await h.Coordinator.TryAcceptAsync(10UL, serverId, 42UL, 5UL, CancellationToken.None);

        Assert.True(ok);

        // The accepted render replaces the prompt in place: same channel, the prompt's message id.
        await h.Poster.Received(1).EnsureAsync(777UL, 900UL, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        Assert.Equal(2, h.PostedEmbeds.Count);
        Assert.Equal("Switch 42", h.PostedEmbeds[1].Title);
        await h.Store.Received(1).SetMessageIdAsync(10UL, serverId, 42UL, 900UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accept_without_a_provisioned_channel_persists_but_posts_nothing()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);
        h.Store.AddAsync(10UL, serverId, 42UL, "Switch 42", 5UL, Arg.Any<CancellationToken>())
            .Returns(new RustPlusBot.Domain.Switches.SmartSwitch
            {
                GuildId = 10UL, ServerId = serverId, EntityId = 42UL, Name = "Switch 42",
            });
        h.Locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)null);

        var ok = await h.Coordinator.TryAcceptAsync(10UL, serverId, 42UL, 5UL, CancellationToken.None);

        Assert.True(ok);
        await h.Store.Received(1).AddAsync(10UL, serverId, 42UL, "Switch 42", 5UL, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        await h.Store.DidNotReceive().SetMessageIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(),
            Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accept_without_a_pending_entry_falls_back_to_the_default_name()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);
        h.Store.AddAsync(10UL, serverId, 42UL, "Switch 42", 5UL, Arg.Any<CancellationToken>())
            .Returns(new RustPlusBot.Domain.Switches.SmartSwitch
            {
                GuildId = 10UL, ServerId = serverId, EntityId = 42UL, Name = "Switch 42",
            });

        var ok = await h.Coordinator.TryAcceptAsync(10UL, serverId, 42UL, 5UL, CancellationToken.None);

        Assert.True(ok);
        await h.Store.Received(1).AddAsync(10UL, serverId, 42UL, "Switch 42", 5UL, Arg.Any<CancellationToken>());

        // No prompt was held, so there is no message to edit: the embed is posted fresh.
        await h.Poster.Received(1).EnsureAsync(777UL, null, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accept_does_not_store_a_message_id_when_the_post_fails()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);
        h.Store.AddAsync(10UL, serverId, 42UL, "Switch 42", 5UL, Arg.Any<CancellationToken>())
            .Returns(new RustPlusBot.Domain.Switches.SmartSwitch
            {
                GuildId = 10UL, ServerId = serverId, EntityId = 42UL, Name = "Switch 42",
            });
        h.Poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
                Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)null);

        var ok = await h.Coordinator.TryAcceptAsync(10UL, serverId, 42UL, 5UL, CancellationToken.None);

        Assert.True(ok);
        await h.Store.DidNotReceive().SetMessageIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(),
            Arg.Any<ulong>(), Arg.Any<CancellationToken>());
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

    [Fact]
    public async Task Accept_race_clears_the_pending_entry()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);
        await h.Coordinator.HandlePairedAsync(new SwitchPairedEvent(10UL, serverId, 42UL), CancellationToken.None);
        Assert.NotNull(h.Coordinator.PendingName(10UL, serverId, 42UL));
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(true);

        var ok = await h.Coordinator.TryAcceptAsync(10UL, serverId, 42UL, 5UL, CancellationToken.None);

        Assert.False(ok);
        Assert.Null(h.Coordinator.PendingName(10UL, serverId, 42UL));
    }

    [Fact]
    public async Task TryDismiss_drops_a_held_pending_entry_and_returns_true()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);
        await h.Coordinator.HandlePairedAsync(new SwitchPairedEvent(10UL, serverId, 42UL), CancellationToken.None);

        Assert.True(h.Coordinator.TryDismiss(10UL, serverId, 42UL));
        Assert.Null(h.Coordinator.PendingName(10UL, serverId, 42UL));
        Assert.False(h.Coordinator.TryDismiss(10UL, serverId, 42UL));
    }

    [Fact]
    public void TryDismiss_no_pending_returns_false() =>
        Assert.False(Create().Coordinator.TryDismiss(10UL, Guid.NewGuid(), 42UL));

    private sealed record Harness(
        SwitchPairingCoordinator Coordinator,
        ISwitchStore Store,
        ISwitchChannelPoster Poster,
        ISwitchChannelLocator Locator,
        IReadOnlyList<global::Discord.Embed> PostedEmbeds);
}
