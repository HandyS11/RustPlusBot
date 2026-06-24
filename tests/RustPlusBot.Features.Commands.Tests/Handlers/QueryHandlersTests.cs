using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Commands.Hosting;
using RustPlusBot.Features.Commands.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class QueryHandlersTests
{
    private static readonly ICommandLocalizer Loc = new CommandLocalizer(CommandLocalizationCatalog.Default);
    private static CommandContext Ctx() => new(1, Guid.NewGuid(), "en", 7, "alice", []);

    [Fact]
    public async Task Pop_FormatsPlayersMaxQueued()
    {
        var query = Substitute.For<IRustServerQuery>();
        var ctx = Ctx();
        query.GetServerInfoAsync(ctx.GuildId, ctx.ServerId, Arg.Any<CancellationToken>())
            .Returns(new ServerInfoSnapshot(5, 100, 2, null));
        var reply = await new PopCommandHandler(query, Loc).ExecuteAsync(ctx, CancellationToken.None);
        Assert.Equal("Pop: 5/100 (2 queued)", reply);
    }

    [Fact]
    public async Task Pop_NotConnected_WhenNull()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetServerInfoAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ServerInfoSnapshot?)null);
        var reply = await new PopCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Not connected to the server.", reply);
    }

    [Fact]
    public async Task Wipe_ReportsAgo()
    {
        var clock = new TestClock
        {
            UtcNow = DateTimeOffset.UnixEpoch.AddDays(2)
        };
        var query = Substitute.For<IRustServerQuery>();
        var ctx = Ctx();
        query.GetServerInfoAsync(ctx.GuildId, ctx.ServerId, Arg.Any<CancellationToken>())
            .Returns(new ServerInfoSnapshot(1, 100, 0, DateTimeOffset.UnixEpoch));
        var reply = await new WipeCommandHandler(query, Loc, clock).ExecuteAsync(ctx, CancellationToken.None);
        Assert.Equal("Wiped 2d 0h ago", reply);
    }

    [Fact]
    public async Task Wipe_Unknown_WhenWipeTimeNull()
    {
        var query = Substitute.For<IRustServerQuery>();
        var ctx = Ctx();
        query.GetServerInfoAsync(ctx.GuildId, ctx.ServerId, Arg.Any<CancellationToken>())
            .Returns(new ServerInfoSnapshot(1, 100, 0, null));
        var reply = await new WipeCommandHandler(query, Loc, new TestClock()).ExecuteAsync(ctx, CancellationToken.None);
        Assert.Equal("Wipe time is unknown.", reply);
    }

    [Fact]
    public async Task Time_ReportsDay_AtNoon()
    {
        var query = Substitute.For<IRustServerQuery>();
        var ctx = Ctx();
        query.GetTimeAsync(ctx.GuildId, ctx.ServerId, Arg.Any<CancellationToken>())
            .Returns(new ServerTimeSnapshot(12f, 7f, 20f)); // noon, day from 7..20
        var reply = await new TimeCommandHandler(query, Loc).ExecuteAsync(ctx, CancellationToken.None);
        Assert.Contains("day", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Time_FormatsClock_FromFractionalHour()
    {
        var query = Substitute.For<IRustServerQuery>();
        var ctx = Ctx();
        query.GetTimeAsync(ctx.GuildId, ctx.ServerId, Arg.Any<CancellationToken>())
            .Returns(new ServerTimeSnapshot(13.5f, 7f, 20f)); // 13:30, day
        var reply = await new TimeCommandHandler(query, Loc).ExecuteAsync(ctx, CancellationToken.None);
        Assert.Equal("Time: 13:30 — day", reply);
    }

    [Fact]
    public async Task Time_ReportsNight_AtMidnight()
    {
        var query = Substitute.For<IRustServerQuery>();
        var ctx = Ctx();
        query.GetTimeAsync(ctx.GuildId, ctx.ServerId, Arg.Any<CancellationToken>())
            .Returns(new ServerTimeSnapshot(2f, 7f, 20f)); // 02:00, before sunrise -> night
        var reply = await new TimeCommandHandler(query, Loc).ExecuteAsync(ctx, CancellationToken.None);
        Assert.Contains("night", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Time_NotConnected_WhenNull()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTimeAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ServerTimeSnapshot?)null);
        var reply = await new TimeCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Not connected to the server.", reply);
    }

    [Fact]
    public async Task Uptime_ReportsBotUptime()
    {
        var clock = new TestClock();
        var uptime = new BotUptime(clock);
        clock.UtcNow = clock.UtcNow.AddHours(3);
        var reply = await new UptimeCommandHandler(uptime, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Uptime: 3h 0m", reply);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }
}
