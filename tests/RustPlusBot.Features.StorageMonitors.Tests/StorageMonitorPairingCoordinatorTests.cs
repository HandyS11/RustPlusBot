using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Features.StorageMonitors.Pairing;
using RustPlusBot.Features.StorageMonitors.Posting;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.StorageMonitors;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.StorageMonitors.Tests;

public sealed class StorageMonitorPairingCoordinatorTests
{
    private static Harness Create()
    {
        var store = Substitute.For<IStorageMonitorStore>();
        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped(_ => workspace);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var locator = Substitute.For<IStorageMonitorChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(777UL);

        var poster = Substitute.For<IStorageMonitorChannelPoster>();
        poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
                Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>())
            .Returns(900UL);

        var names = Substitute.For<IItemNameResolver>();
        names.Resolve(Arg.Any<int>()).Returns(ci => "Item" + (int)ci[0]);
        var renderer = new StorageMonitorEmbedRenderer(new ResxLocalizer(), names);
        var coordinator = new StorageMonitorPairingCoordinator(scopeFactory, locator, poster, renderer);
        return new Harness(coordinator, store, poster, locator);
    }

    [Fact]
    public async Task Paired_new_monitor_posts_prompt_with_default_name()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);

        await h.Coordinator.HandlePairedAsync(new StorageMonitorPairedEvent(10UL, serverId, 42UL),
            CancellationToken.None);

        await h.Poster.Received(1).EnsureAsync(777UL, null, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        Assert.Equal("Storage Monitor 42", h.Coordinator.PendingName(10UL, serverId, 42UL));
    }

    [Fact]
    public async Task Paired_already_managed_monitor_is_ignored()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(true);

        await h.Coordinator.HandlePairedAsync(new StorageMonitorPairedEvent(10UL, serverId, 42UL),
            CancellationToken.None);

        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        Assert.Null(h.Coordinator.PendingName(10UL, serverId, 42UL));
    }

    [Fact]
    public async Task Accept_persists_monitor_and_replaces_prompt()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);
        await h.Coordinator.HandlePairedAsync(new StorageMonitorPairedEvent(10UL, serverId, 42UL),
            CancellationToken.None);
        h.Store.AddAsync(10UL, serverId, 42UL, "Storage Monitor 42", 5UL, Arg.Any<CancellationToken>())
            .Returns(new SmartStorageMonitor
            {
                GuildId = 10UL, ServerId = serverId, EntityId = 42UL, Name = "Storage Monitor 42",
            });

        var ok = await h.Coordinator.TryAcceptAsync(10UL, serverId, 42UL, acceptingUserId: 5UL,
            CancellationToken.None);

        Assert.True(ok);
        await h.Store.Received(1).AddAsync(10UL, serverId, 42UL, "Storage Monitor 42", 5UL,
            Arg.Any<CancellationToken>());
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

    [Fact]
    public void TryDismiss_no_pending_returns_false() =>
        Assert.False(Create().Coordinator.TryDismiss(10UL, Guid.NewGuid(), 42UL));

    private sealed record Harness(
        StorageMonitorPairingCoordinator Coordinator,
        IStorageMonitorStore Store,
        IStorageMonitorChannelPoster Poster,
        IStorageMonitorChannelLocator Locator);
}
