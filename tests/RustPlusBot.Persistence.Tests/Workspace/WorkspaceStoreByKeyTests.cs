using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Persistence.Tests.Workspace;

public sealed class WorkspaceStoreByKeyTests
{
    private static WorkspaceStore NewStore(out BotDbContext context, out IDisposable cleanup)
    {
        var (ctx, connection) = SqliteContextFixture.Create(new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        context = ctx;
        cleanup = connection;
        return new WorkspaceStore(ctx);
    }

    [Fact]
    public async Task GetChannelsByKeyAsync_returns_all_rows_with_that_key()
    {
        var store = NewStore(out var context, out var cleanup);
        using var _cleanup = cleanup;

        // RustServerId is a FK to RustServers, so insert a real server first.
        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 10UL, RustServerId = server.Id, ChannelKey = "teamchat", DiscordChannelId = 777UL,
        });
        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 10UL, RustServerId = server.Id, ChannelKey = "info", DiscordChannelId = 888UL,
        });

        var rows = await store.GetChannelsByKeyAsync("teamchat");

        Assert.Single(rows);
        Assert.Equal(777UL, rows[0].DiscordChannelId);
    }

    [Fact]
    public async Task GetChannelsByKeyAsync_returns_rows_across_multiple_guilds()
    {
        var store = NewStore(out _, out var cleanup);
        using var _cleanup = cleanup;

        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 1UL, RustServerId = null, ChannelKey = "teamchat", DiscordChannelId = 100UL,
        });
        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 2UL, RustServerId = null, ChannelKey = "teamchat", DiscordChannelId = 200UL,
        });
        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 1UL, RustServerId = null, ChannelKey = "info", DiscordChannelId = 300UL,
        });

        var rows = await store.GetChannelsByKeyAsync("teamchat");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("teamchat", r.ChannelKey));
    }

    [Fact]
    public async Task GetChannelsByKeyAsync_returns_empty_when_no_match()
    {
        var store = NewStore(out _, out var cleanup);
        using var _cleanup = cleanup;

        var rows = await store.GetChannelsByKeyAsync("teamchat");

        Assert.Empty(rows);
    }
}
