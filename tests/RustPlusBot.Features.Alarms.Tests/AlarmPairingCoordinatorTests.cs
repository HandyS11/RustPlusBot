using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Features.Alarms.Pairing;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Alarms.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Alarms;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Alarms.Tests;

public sealed class AlarmPairingCoordinatorTests
{
    private static Harness Create()
    {
        var store = Substitute.For<IAlarmStore>();
        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped(_ => workspace);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var locator = Substitute.For<IAlarmChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(777UL);

        var poster = Substitute.For<IAlarmChannelPoster>();
        poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
                Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>())
            .Returns(900UL);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var localizer = new ResxLocalizer();
        var renderer = new AlarmEmbedRenderer(localizer, clock);

        var coordinator = new AlarmPairingCoordinator(scopeFactory, locator, poster, renderer);
        return new Harness(coordinator, store, poster, locator);
    }

    [Fact]
    public async Task Paired_new_alarm_posts_prompt_with_default_name()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);

        await h.Coordinator.HandlePairedAsync(new AlarmPairedEvent(10UL, serverId, 42UL), CancellationToken.None);

        await h.Poster.Received(1).EnsureAsync(777UL, null, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        Assert.Equal("Alarm 42", h.Coordinator.PendingName(10UL, serverId, 42UL));
    }

    [Fact]
    public async Task Paired_already_managed_alarm_is_ignored()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(true);

        await h.Coordinator.HandlePairedAsync(new AlarmPairedEvent(10UL, serverId, 42UL), CancellationToken.None);

        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        Assert.Null(h.Coordinator.PendingName(10UL, serverId, 42UL));
    }

    [Fact]
    public async Task Accept_persists_alarm_and_replaces_prompt()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);
        await h.Coordinator.HandlePairedAsync(new AlarmPairedEvent(10UL, serverId, 42UL), CancellationToken.None);
        h.Store.AddAsync(10UL, serverId, 42UL, "Alarm 42", 5UL, Arg.Any<CancellationToken>())
            .Returns(new SmartAlarm
            {
                GuildId = 10UL, ServerId = serverId, EntityId = 42UL, Name = "Alarm 42",
            });

        var ok = await h.Coordinator.TryAcceptAsync(10UL, serverId, 42UL, acceptingUserId: 5UL, CancellationToken.None);

        Assert.True(ok);
        await h.Store.Received(1).AddAsync(10UL, serverId, 42UL, "Alarm 42", 5UL, Arg.Any<CancellationToken>());
        Assert.Null(h.Coordinator.PendingName(10UL, serverId, 42UL)); // pending cleared
        // The prompt (posted by HandlePairedAsync) must be replaced by the alarm embed (posted by TryAcceptAsync).
        // EnsureAsync is called twice: once for the prompt (messageId=null), once for the embed (messageId=900 from prompt).
        await h.Poster.Received(2).EnsureAsync(777UL, Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        await h.Poster.Received(1).EnsureAsync(777UL, 900UL, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
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
    public async Task TryDismiss_removes_pending_and_returns_true()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ExistsAsync(10UL, serverId, 42UL, Arg.Any<CancellationToken>()).Returns(false);
        await h.Coordinator.HandlePairedAsync(new AlarmPairedEvent(10UL, serverId, 42UL), CancellationToken.None);

        var dismissed = h.Coordinator.TryDismiss(10UL, serverId, 42UL);

        Assert.True(dismissed);
        Assert.Null(h.Coordinator.PendingName(10UL, serverId, 42UL));
    }

    [Fact]
    public void TryDismiss_no_pending_returns_false()
    {
        var h = Create();
        var serverId = Guid.NewGuid();

        var dismissed = h.Coordinator.TryDismiss(10UL, serverId, 42UL);

        Assert.False(dismissed);
    }

    private sealed record Harness(
        AlarmPairingCoordinator Coordinator,
        IAlarmStore Store,
        IAlarmChannelPoster Poster,
        IAlarmChannelLocator Locator);
}
