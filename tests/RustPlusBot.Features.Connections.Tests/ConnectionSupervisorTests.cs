using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Connections.Tests.Fakes;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class ConnectionSupervisorTests
{
    private static Harness CreateHarness(FakeRustSocketSource source)
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Unprotect(Arg.Any<string>()).Returns(c => c.Arg<string>());

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var dm = Substitute.For<IUserDmSender>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton(protector);
        services.AddSingleton(dm);
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // Each scope opens its OWN connection to a shared-cache in-memory database, so the background
        // supervisor loop and the test's polling never run concurrent commands on a single SqliteConnection
        // (which throws "active statements" misuse errors). One kept-open connection keeps the in-memory DB alive.
        var connectionString = $"DataSource=connsup-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
        using (var seed = new BotDbContext(
                   new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connectionString).Options))
        {
            seed.Database.Migrate();
        }

        services.AddSingleton(keepAlive);
        services.AddScoped(_ => new BotDbContext(
            new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connectionString).Options));
        services.AddScoped<IConnectionStore, ConnectionStore>();
        services
            .AddScoped<RustPlusBot.Persistence.Servers.IServerService, RustPlusBot.Persistence.Servers.ServerService>();

        services.AddSingleton<IRustSocketSource>(source);
        services.AddSingleton(Options.Create(new ConnectionOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(1),
            InitialRetryDelay = TimeSpan.FromMilliseconds(5),
            MaxRetryDelay = TimeSpan.FromMilliseconds(20),
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatTimeout = TimeSpan.FromMilliseconds(200),
        }));
        services.AddSingleton<ConnectionSupervisor>();

        var provider = services.BuildServiceProvider();
        return new Harness
        {
            Provider = provider, Dm = dm, Supervisor = provider.GetRequiredService<ConnectionSupervisor>()
        };
    }

    private static async Task<(Guid ServerId, Guid CredA, Guid CredB)> SeedAsync(
        ServiceProvider provider,
        CredentialStatus bStatus = CredentialStatus.Standby)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
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
            ProtectedPlayerToken = "111",
            Status = CredentialStatus.Active,
        };
        var b = new PlayerCredential
        {
            GuildId = 10UL,
            RustServerId = server.Id,
            OwnerUserId = 2UL,
            SteamId = 200UL,
            ProtectedPlayerToken = "222",
            Status = bStatus,
        };
        await context.PlayerCredentials.AddRangeAsync(a, b);
        await context.SaveChangesAsync();
        return (server.Id, a.Id, b.Id);
    }

    private static async Task<ConnectionState?> WaitForStateAsync(
        ServiceProvider provider,
        Guid serverId,
        Func<ConnectionState, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = provider.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var state = await store.GetStateAsync(10UL, serverId);
            if (state is not null && predicate(state))
            {
                return state;
            }

            await Task.Delay(15);
        }

        return null;
    }

    private static async Task<CredentialStatus> CredStatusAsync(ServiceProvider provider, Guid credId)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        return (await context.PlayerCredentials.SingleAsync(c => c.Id == credId)).Status;
    }

    [Fact]
    public async Task Connect_Healthy_BecomesConnectedWithPlayerCount()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(7));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        Assert.Equal(7, state!.PlayerCount);
    }

    [Fact]
    public async Task Connect_AuthRejected_FailsOverToNextStandbyAndDmsOwner()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.AuthRejected); // credential A
        source.EnqueueConnect(SocketConnectOutcome.Connected); // credential B
        source.EnqueueHeartbeat(HeartbeatResult.Ok(3));
        await using var h = CreateHarness(source);
        var (serverId, credA, credB) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        Assert.Equal(CredentialStatus.Invalid, await CredStatusAsync(h.Provider, credA));
        Assert.Equal(CredentialStatus.Active, await CredStatusAsync(h.Provider, credB));
        await h.Dm.Received(1).SendAsync(1UL, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_AllCredentialsRejected_BecomesNoCredentials()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.AuthRejected); // only credential, A
        await using var h = CreateHarness(source);
        var (serverId, credA, _) = await SeedAsync(h.Provider, bStatus: CredentialStatus.Invalid);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.NoCredentials);
        Assert.NotNull(state);
        Assert.Equal(CredentialStatus.Invalid, await CredStatusAsync(h.Provider, credA));
    }

    [Fact]
    public async Task Connect_UnreachableThenConnected_RetriesAndRecovers()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Unreachable);
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        Assert.True(source.CreateCount >= 2);
    }

    [Fact]
    public async Task Heartbeat_Unreachable_ReconnectsAndRecovers()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(2)); // first heartbeat -> Connected
        source.EnqueueHeartbeat(HeartbeatResult.Unreachable); // next heartbeat -> drop
        source.EnqueueConnect(SocketConnectOutcome.Connected); // reconnect
        source.EnqueueHeartbeat(HeartbeatResult.Ok(4));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var recovered = await WaitForStateAsync(
            h.Provider, serverId, s => s.Status == ConnectionStatus.Connected && s.PlayerCount == 4);
        Assert.NotNull(recovered);
        Assert.True(source.CreateCount >= 2);
    }

    [Fact]
    public async Task StartAll_StartsAConnectionPerConnectableServer()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedAsync(h.Provider);

        await h.Supervisor.StartAllAsync();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (source.CreateCount < 1 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(15);
        }

        Assert.True(source.CreateCount >= 1);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required IUserDmSender Dm { get; init; }
        public required ConnectionSupervisor Supervisor { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Supervisor.StopAllAsync();
            await Provider.DisposeAsync();
        }
    }
}
