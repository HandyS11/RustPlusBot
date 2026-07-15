using Microsoft.Data.Sqlite;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Wipes;

namespace RustPlusBot.Persistence.Tests.Wipes;

/// <summary>Unit tests for <see cref="WipeBaselineStore"/>.</summary>
public sealed class WipeBaselineStoreTests
{
    private static (WipeBaselineStore Store, BotDbContext Context, SqliteConnection Conn) Create()
    {
        var (context, connection) = SqliteContextFixture.Create();
        return (new WipeBaselineStore(context), context, connection);
    }

    private static async Task<Guid> SeedServerAsync(BotDbContext context)
    {
        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }

    [Fact]
    public async Task Get_returns_null_for_unknown_server()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;

        var baseline = await store.GetAsync(10UL, Guid.NewGuid());

        Assert.Null(baseline);
    }

    [Fact]
    public async Task Get_returns_empty_baseline_for_new_server()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        var baseline = await store.GetAsync(10UL, serverId);

        Assert.Equal(new WipeBaseline(null, null, null), baseline);
    }

    [Fact]
    public async Task Set_then_Get_round_trips()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        var baseline = new WipeBaseline(new DateTimeOffset(2026, 7, 2, 18, 0, 0, TimeSpan.Zero), 42u, 3500u);

        await store.SetAsync(10UL, serverId, baseline);

        Assert.Equal(baseline, await store.GetAsync(10UL, serverId));
    }

    [Fact]
    public async Task Set_is_noop_for_unknown_server()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;

        await store.SetAsync(10UL, Guid.NewGuid(), new WipeBaseline(null, 42u, null));

        // No throw; nothing persisted.
        Assert.Null(await store.GetAsync(10UL, Guid.NewGuid()));
    }

    [Fact]
    public async Task Get_is_guild_scoped()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        var baseline = await store.GetAsync(999UL, serverId);

        Assert.Null(baseline);
    }
}
