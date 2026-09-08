using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Persistence.Tests.Connections;

public sealed class ConnectionStoreTests
{
    private static BotDbContext NewContext(SqliteConnection connection, IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        var context = new BotDbContext(options);
        context.Database.Migrate();
        return context;
    }

    private static (ConnectionStore Store, BotDbContext Context, SqliteConnection Conn) Create()
    {
        var (context, connection) = SqliteContextFixture.Create();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        return (new ConnectionStore(context, clock), context, connection);
    }

    private static async Task<(Guid ServerId, Guid CredA, Guid CredB)> SeedServerWithPoolAsync(BotDbContext context)
    {
        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        var a = new PlayerCredential
        {
            GuildId = 10UL,
            RustServerId = server.Id,
            OwnerUserId = 1UL,
            SteamId = 100UL,
            ProtectedPlayerToken = "ta",
            Status = CredentialStatus.Active,
        };
        var b = new PlayerCredential
        {
            GuildId = 10UL,
            RustServerId = server.Id,
            OwnerUserId = 2UL,
            SteamId = 200UL,
            ProtectedPlayerToken = "tb",
            Status = CredentialStatus.Standby,
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
        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        var serverId = server.Id;

        Assert.True(await store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Connecting, null, null));
        Assert.True(await store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Connected, 5, null));
        Assert.False(await store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Connected, 5, null));
        Assert.True(await store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Connected, 6, null));

        var state = await store.GetStateAsync(10UL, serverId);
        Assert.Equal(ConnectionStatus.Connected, state!.Status);
        Assert.Equal(6, state.PlayerCount);
    }

    /// <summary>
    /// A connection loop can still be running when its server row is deleted (a guild purge racing the
    /// loop). The status row is FK'd to RustServers, so inserting one for a server that is gone throws a
    /// constraint violation and kills the loop. A deleted server has no status to record: write nothing.
    /// </summary>
    [Fact]
    public async Task UpsertStatus_ForAServerThatIsGone_WritesNothingAndReportsNoChange()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;

        var changed = await store.UpsertStatusAsync(
            10UL, Guid.NewGuid(), ConnectionStatus.NoCredentials, null, null);

        Assert.False(changed);
        Assert.Empty(await context.ConnectionStates.ToListAsync());
    }

    /// <summary>
    /// The existence check and the insert are two separate round trips, so the server row can still be
    /// deleted in between. The foreign-key violation that follows must not escape: it would fault the
    /// connection loop that called in, which is the very outcome the check exists to prevent.
    /// </summary>
    [Fact]
    public async Task UpsertStatus_WhenTheServerIsDeletedMidSave_WritesNothingAndReportsNoChange()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var _ = connection;

        var deleter = new InterferingWriteInterceptor((ctx, ct) =>
            ctx.Database.ExecuteSqlRawAsync("DELETE FROM RustServers", ct));
        await using var context = NewContext(connection, deleter);

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        // Armed only now: the seed above must survive, and the delete must land between the store's
        // existence check and its insert.
        deleter.Arm();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var store = new ConnectionStore(context, clock);

        var changed = await store.UpsertStatusAsync(
            10UL, server.Id, ConnectionStatus.Unreachable, null, null);

        Assert.False(changed);
        Assert.Empty(await context.ConnectionStates.ToListAsync());
    }

    /// <summary>
    /// Only a vanished parent may be swallowed. Any other write failure — here a status row inserted
    /// concurrently, colliding on the primary key — is a real store problem and must reach the caller
    /// rather than being reported as "nothing changed".
    /// </summary>
    [Fact]
    public async Task UpsertStatus_WhenTheSaveFailsForAnotherReason_Throws()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var _ = connection;

        var serverId = Guid.Empty;
        var conflicter = new InterferingWriteInterceptor(async (ctx, ct) =>
        {
            // A second context over the SAME connection, so the row lands before the outer insert runs.
            var options = new DbContextOptionsBuilder<BotDbContext>()
                .UseSqlite(ctx.Database.GetDbConnection())
                .Options;
            await using var other = new BotDbContext(options);
            other.ConnectionStates.Add(new ConnectionState
            {
                RustServerId = serverId, GuildId = 10UL, Status = ConnectionStatus.Connected
            });
            await other.SaveChangesAsync(ct);
        });
        await using var context = NewContext(connection, conflicter);

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        serverId = server.Id;

        conflicter.Arm();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var store = new ConnectionStore(context, clock);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Unreachable, null, null));
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

    [Fact]
    public async Task GetStatesForGuild_ReturnsOnlyThatGuildsStates()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;

        var serverA = new RustServer
        {
            GuildId = 10UL, Name = "A", Ip = "1.1.1.1", Port = 28015
        };
        var serverB = new RustServer
        {
            GuildId = 20UL, Name = "B", Ip = "2.2.2.2", Port = 28015
        };
        context.RustServers.AddRange(serverA, serverB);
        await context.SaveChangesAsync();
        await store.UpsertStatusAsync(10UL, serverA.Id, ConnectionStatus.Connected, 5, null);
        await store.UpsertStatusAsync(20UL, serverB.Id, ConnectionStatus.Connecting, null, null);

        var states = await store.GetStatesForGuildAsync(10UL);

        Assert.Equal(serverA.Id, Assert.Single(states).RustServerId);
        Assert.Empty(await store.GetStatesForGuildAsync(30UL));
    }

    /// <summary>
    /// Runs <paramref name="action"/> from inside SaveChanges, once armed — the seam for reproducing a
    /// concurrent write that lands between a caller's read and its own SaveChanges.
    /// </summary>
    /// <param name="action">The interfering write, given the saving context.</param>
    private sealed class InterferingWriteInterceptor(Func<BotDbContext, CancellationToken, Task> action)
        : SaveChangesInterceptor
    {
        private bool _armed;

        public void Arm() => _armed = true;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_armed && eventData.Context is BotDbContext context)
            {
                _armed = false;
                await action(context, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
    }
}
