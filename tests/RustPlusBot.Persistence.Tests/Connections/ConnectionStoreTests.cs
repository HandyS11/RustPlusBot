using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Persistence.Tests.Connections;

public sealed class ConnectionStoreTests
{
    private static (ConnectionStore Store, BotDbContext Context, SqliteConnection Conn) Create()
    {
        var (context, connection) = SqliteContextFixture.Create();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        return (new ConnectionStore(context, clock), context, connection);
    }

    private static async Task<(Guid ServerId, Guid CredA, Guid CredB)> SeedServerWithPoolAsync(BotDbContext context)
    {
        var server = new RustServer { GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015 };
        context.RustServers.Add(server);
        var a = new PlayerCredential
        {
            GuildId = 10UL, RustServerId = server.Id, OwnerUserId = 1UL, SteamId = 100UL,
            ProtectedPlayerToken = "ta", Status = CredentialStatus.Active,
        };
        var b = new PlayerCredential
        {
            GuildId = 10UL, RustServerId = server.Id, OwnerUserId = 2UL, SteamId = 200UL,
            ProtectedPlayerToken = "tb", Status = CredentialStatus.Standby,
        };
        await context.PlayerCredentials.AddRangeAsync(a, b);
        await context.SaveChangesAsync();
        return (server.Id, a.Id, b.Id);
    }

    [Fact]
    public async Task UpsertStatus_InsertsThenReportsChangeOnlyWhenDifferent()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = Guid.NewGuid();

        Assert.True(await store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Connecting, null, null));
        Assert.True(await store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Connected, 5, null));
        Assert.False(await store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Connected, 5, null));
        Assert.True(await store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Connected, 6, null));

        var state = await store.GetStateAsync(10UL, serverId);
        Assert.Equal(ConnectionStatus.Connected, state!.Status);
        Assert.Equal(6, state.PlayerCount);
    }

    [Fact]
    public async Task GetActiveCredential_ReturnsTheActiveOne()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var (serverId, credA, _) = await SeedServerWithPoolAsync(context);

        var active = await store.GetActiveCredentialAsync(10UL, serverId);

        Assert.Equal(credA, active!.Id);
        Assert.Equal("ta", active.ProtectedPlayerToken);
    }

    [Fact]
    public async Task Promote_MakesTargetActiveAndDemotesPrior()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var (serverId, credA, credB) = await SeedServerWithPoolAsync(context);

        var ok = await store.PromoteAsync(10UL, serverId, credB);

        Assert.True(ok);
        var pool = await store.ListPoolAsync(10UL, serverId);
        Assert.Equal(CredentialStatus.Active, pool.Single(c => c.Id == credB).Status);
        Assert.Equal(CredentialStatus.Standby, pool.Single(c => c.Id == credA).Status);
    }

    [Fact]
    public async Task Promote_RejectsInvalidOrUnknownCredential()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var (serverId, credA, _) = await SeedServerWithPoolAsync(context);
        await store.MarkInvalidAsync(credA);

        Assert.False(await store.PromoteAsync(10UL, serverId, credA));
        Assert.False(await store.PromoteAsync(10UL, serverId, Guid.NewGuid()));
    }

    [Fact]
    public async Task MarkInvalid_SetsStatusInvalid()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var (serverId, credA, _) = await SeedServerWithPoolAsync(context);

        await store.MarkInvalidAsync(credA);

        var pool = await store.ListPoolAsync(10UL, serverId);
        Assert.Equal(CredentialStatus.Invalid, pool.Single(c => c.Id == credA).Status);
    }

    [Fact]
    public async Task ListConnectableServers_ExcludesAllInvalidServers()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var (serverId, credA, credB) = await SeedServerWithPoolAsync(context);

        var before = await store.ListConnectableServersAsync();
        Assert.Contains((10UL, serverId), before);

        await store.MarkInvalidAsync(credA);
        await store.MarkInvalidAsync(credB);

        var after = await store.ListConnectableServersAsync();
        Assert.DoesNotContain((10UL, serverId), after);
    }
}
