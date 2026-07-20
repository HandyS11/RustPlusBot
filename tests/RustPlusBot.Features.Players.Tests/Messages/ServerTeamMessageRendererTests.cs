using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Players.Messages;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Players.Tests.Messages;

public sealed class ServerTeamMessageRendererTests
{
    private static readonly ResxLocalizer Loc = new();
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedOrder = ["Alice", "Bob", "Carol", "Dave"];

    private static TeamMemberSnapshot Member(
        ulong steamId,
        string name,
        bool online,
        bool alive,
        double spawnedHoursAgo = 2,
        double diedMinutesAgo = 0)
        => new(steamId, name, 2000f, 2000f, online, alive,
            Now.AddHours(-spawnedHoursAgo), Now.AddMinutes(-diedMinutesAgo));

    private static ServerTeamMessageRenderer Build(IRustServerQuery query, IAfkState afk)
    {
        var mapSettings = Substitute.For<IMapSettingsStore>();
        mapSettings.GetAsync(1, ServerId, Arg.Any<CancellationToken>()).Returns(MapLayerSettings.AllOn);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ServerTeamMessageRenderer(query, afk, mapSettings, clock, Loc);
    }

    private static IAfkState NoAfk()
    {
        var afk = Substitute.For<IAfkState>();
        afk.GetAfkMembersAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AfkMember>?>([]));
        return afk;
    }

    [Fact]
    public async Task Title_counts_online_over_total()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(1UL, [
                Member(1UL, "Alice", online: true, alive: true),
                Member(2UL, "Bob", online: true, alive: true),
                Member(3UL, "Carol", online: false, alive: true),
            ]));

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.Contains("2/3 online", payload.Embed!.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leader_gets_a_crown()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(2UL, [
                Member(1UL, "Alice", online: true, alive: true),
                Member(2UL, "Bob", online: true, alive: true),
            ]));

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        var bobLine = payload.Embed!.Description.Split('\n').Single(l => l.Contains("Bob", StringComparison.Ordinal));
        Assert.Contains("👑", bobLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Afk_member_renders_afk_not_alive()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(1UL, [Member(1UL, "Alice", online: true, alive: true)]));
        var afk = Substitute.For<IAfkState>();
        afk.GetAfkMembersAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AfkMember>?>(
                [new AfkMember(1UL, "Alice", TimeSpan.FromMinutes(7))]));

        var payload = await Build(query, afk).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.Contains("AFK 7m", payload.Embed!.Description, StringComparison.Ordinal);
        Assert.Contains("😴", payload.Embed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dead_member_renders_death_age()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(1UL,
                [Member(1UL, "Alice", online: true, alive: false, diedMinutesAgo: 6)]));

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.Contains("dead 6m", payload.Embed!.Description, StringComparison.Ordinal);
        Assert.Contains("💀", payload.Embed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offline_member_hides_grid()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(1UL, [Member(1UL, "Alice", online: false, alive: true)]));

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        var line = payload.Embed!.Description.Split('\n').Single(l => l.Contains("Alice", StringComparison.Ordinal));
        Assert.Contains("—", line, StringComparison.Ordinal);
        Assert.Contains("⚫", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_name_falls_back_to_steam_id()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(1UL, [Member(76561198000000000UL, "", online: true, alive: true)]));

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.Contains("76561198000000000", payload.Embed!.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zero_members_renders_empty_notice()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(0UL, []));

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.Contains("0/0 online", payload.Embed!.Title, StringComparison.Ordinal);
        Assert.Equal("No team members.", payload.Embed.Description);
    }

    [Fact]
    public async Task Null_snapshot_renders_disconnected_notice()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns((TeamInfoSnapshot?)null);

        var payload = await Build(query, NoAfk()).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.Equal("Not connected to the server.", payload.Embed!.Description);
    }

    [Fact]
    public async Task Online_alive_sort_before_afk_dead_and_offline()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(99UL, [
                Member(4UL, "Dave", online: false, alive: true),
                Member(3UL, "Carol", online: true, alive: false, diedMinutesAgo: 6),
                Member(2UL, "Bob", online: true, alive: true, spawnedHoursAgo: 1),
                Member(1UL, "Alice", online: true, alive: true, spawnedHoursAgo: 3),
            ]));
        var afk = Substitute.For<IAfkState>();
        afk.GetAfkMembersAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AfkMember>?>(
                [new AfkMember(2UL, "Bob", TimeSpan.FromMinutes(7))]));

        var payload = await Build(query, afk).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        var names = payload.Embed!.Description.Split('\n')
            .Select(l => ExpectedOrder.First(n => l.Contains(n, StringComparison.Ordinal)))
            .ToArray();
        Assert.Equal(ExpectedOrder, names);
    }
}
