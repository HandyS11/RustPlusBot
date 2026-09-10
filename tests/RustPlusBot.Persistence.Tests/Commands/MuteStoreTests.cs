using Persistord.Testing;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Persistence.Tests.Commands;

public sealed class MuteStoreTests
{
    private static (MuteStore Store, BotDbContext Context, SqliteTestDatabase Db) Create()
    {
        var (context, connection) = SqliteContextFixture.Create();
        return (new MuteStore(context), context, connection);
    }

    private static async Task<Guid> SeedServerAsync(BotDbContext context)
    {
        var server = new RustServer
        {
            GuildId = 1UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }

    [Fact]
    public async Task GetMuted_DefaultsFalse_WhenNoRow()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        Assert.False(await store.GetMutedAsync(1UL, serverId, CancellationToken.None));
    }

    [Fact]
    public async Task GetPrefix_DefaultsBang_WhenNoRow()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        Assert.Equal("!", await store.GetPrefixAsync(1UL, serverId, CancellationToken.None));
    }

    [Fact]
    public async Task SetMuted_PersistsAndReadsBack()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        await store.SetMutedAsync(1UL, serverId, true, CancellationToken.None);
        Assert.True(await store.GetMutedAsync(1UL, serverId, CancellationToken.None));

        await store.SetMutedAsync(1UL, serverId, false, CancellationToken.None);
        Assert.False(await store.GetMutedAsync(1UL, serverId, CancellationToken.None));
    }
}
