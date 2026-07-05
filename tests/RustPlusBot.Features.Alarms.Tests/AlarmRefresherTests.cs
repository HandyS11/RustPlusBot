using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Alarms.Relaying;
using RustPlusBot.Features.Alarms.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Alarms;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Alarms.Tests;

public sealed class AlarmRefresherTests
{
    private static readonly DateTimeOffset _fixedNow = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static Harness Create(SmartAlarm? alarm = null, ulong? channelId = 777UL)
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
            .Returns(channelId);

        var poster = Substitute.For<IAlarmChannelPoster>();
        poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
                Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>())
            .Returns(900UL);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_fixedNow);
        var localizer = new ResxLocalizer();
        var renderer = new AlarmEmbedRenderer(localizer);

        if (alarm is not null)
        {
            store.GetAsync(alarm.GuildId, alarm.ServerId, alarm.EntityId, Arg.Any<CancellationToken>())
                .Returns(alarm);
        }

        var refresher = new AlarmRefresher(scopeFactory, locator, poster, renderer);
        return new Harness(refresher, store, poster, locator);
    }

    [Fact]
    public async Task RefreshAsync_alarm_present_and_channel_resolved_calls_render_and_ensure()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL,
            ServerId = serverId,
            EntityId = 42UL,
            Name = "Fire Alarm",
            MessageId = 800UL,
        };
        var h = Create(alarm: alarm, channelId: 777UL);

        await h.Refresher.RefreshAsync(10UL, serverId, 42UL, unreachable: false, CancellationToken.None);

        await h.Poster.Received(1).EnsureAsync(777UL, 800UL, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_alarm_present_new_message_id_persisted()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL,
            ServerId = serverId,
            EntityId = 42UL,
            Name = "Fire Alarm",
            MessageId = 800UL,
        };
        var h = Create(alarm: alarm, channelId: 777UL);
        // EnsureAsync returns 900 (different from MessageId 800) — SetMessageIdAsync must be called
        h.Poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
                Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>())
            .Returns(900UL);

        await h.Refresher.RefreshAsync(10UL, serverId, 42UL, unreachable: false, CancellationToken.None);

        await h.Store.Received(1).SetMessageIdAsync(10UL, serverId, 42UL, 900UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_alarm_absent_does_not_post()
    {
        var serverId = Guid.NewGuid();
        // store returns null by default (not configured for this identity)
        var h = Create(alarm: null, channelId: 777UL);

        await h.Refresher.RefreshAsync(10UL, serverId, 42UL, unreachable: false, CancellationToken.None);

        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_channel_unresolved_does_not_post()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL, ServerId = serverId, EntityId = 42UL, Name = "Fire Alarm",
        };
        var h = Create(alarm: alarm, channelId: null);

        await h.Refresher.RefreshAsync(10UL, serverId, 42UL, unreachable: false, CancellationToken.None);

        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_loaded_alarm_posts_without_refetching()
    {
        var serverId = Guid.NewGuid();
        var alarm = new SmartAlarm
        {
            GuildId = 10UL,
            ServerId = serverId,
            EntityId = 42UL,
            Name = "Fire Alarm",
            MessageId = 800UL,
        };
        // Note: store.GetAsync is intentionally NOT configured — the overload must not call it.
        var h = Create(alarm: null, channelId: 777UL);

        await h.Refresher.RefreshAsync(alarm, unreachable: true, CancellationToken.None);

        await h.Store.DidNotReceive().GetAsync(
            Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>());
        await h.Poster.Received(1).EnsureAsync(777UL, 800UL, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }

    private sealed record Harness(
        AlarmRefresher Refresher,
        IAlarmStore Store,
        IAlarmChannelPoster Poster,
        IAlarmChannelLocator Locator);
}
