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
            MarkerPollInterval = TimeSpan.FromMilliseconds(20),
        }));
        services.AddSingleton<ConnectionSupervisor>();

        var provider = services.BuildServiceProvider();
        return new Harness
        {
            Provider = provider,
            Dm = dm,
            Supervisor = provider.GetRequiredService<ConnectionSupervisor>(),
            Bus = provider.GetRequiredService<IEventBus>(),
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

    [Fact]
    public async Task First_marker_poll_is_a_silent_baseline()
    {
        // Contract: the FIRST poll after connect must not publish any event even when markers are
        // present on that first poll (the baseline records them silently). Only LATER changes alert.
        //
        // Script:
        //   Poll 1 → [CargoShip]     (baseline — must NOT fire an event)
        //   Poll 2 → [CargoShip]     (no change — still no event)
        //   Poll 3 → [CargoShip, PatrolHelicopter]  (heli added — ONE event, Added=[heli])
        //
        // Waiting for the definite heli-event signal proves both that baseline suppression held AND
        // that the diff works, without any fixed-sleep assertion.
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<MapMarkersChangedEvent>();
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in h.Bus.SubscribeAsync<MapMarkersChangedEvent>(cts.Token))
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);

        // Script the three polls before EnsureConnectionAsync so the script is in place before the
        // poll loop starts — no race between test setup and the supervisor's background poll task.
        var cargo = new MapMarkerSnapshot(1UL, MarkerKind.CargoShip, 1f, 1f, null);
        var heli = new MapMarkerSnapshot(2UL, MarkerKind.PatrolHelicopter, 2f, 2f, null);
        source.EnqueueMarkers([cargo]); // poll 1: baseline (silent)
        source.EnqueueMarkers([cargo]); // poll 2: no change
        source.EnqueueMarkers([cargo, heli]); // poll 3: heli added → one event

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        // Wait for the heli-added event — its arrival is the definite signal that at least three
        // poll cycles have completed and proves the baseline CargoShip never triggered an event.
        await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);

        Assert.Single(captured);
        Assert.True(captured.TryPeek(out var evt));
        Assert.Single(evt!.Added);
        Assert.Equal(MarkerKind.PatrolHelicopter, evt.Added[0].Kind);
        Assert.Empty(evt.Removed);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try { await subTask; }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task Marker_added_on_a_later_poll_publishes_changed_event()
    {
        // Contract: first poll is a silent baseline; a new marker on a subsequent poll fires exactly
        // one MapMarkersChangedEvent with the correct Added entry and the connect-time dimensions.
        //
        // Script:
        //   Poll 1 → []              (baseline — no event)
        //   Poll 2 → [CargoShip]     (added → one event)
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<MapMarkersChangedEvent>();
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in h.Bus.SubscribeAsync<MapMarkersChangedEvent>(cts.Token))
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);

        // FakeConnection default DimensionsResult is new(4000u, 4000u, 500); assert those exact values.
        var expectedDims = new MapDimensions(4000u, 4000u, 500);

        // Script polls before EnsureConnectionAsync so the marker script is in the connection before
        // the poll loop can start — eliminates any setup race.
        source.EnqueueMarkers([]); // poll 1: baseline
        source.EnqueueMarkers([new MapMarkerSnapshot(2UL, MarkerKind.CargoShip, 1f, 1f, "Cargo A")]); // poll 2

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        // Wait for the definite signal: the CargoShip-added event.
        await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);

        Assert.Single(captured);
        Assert.True(captured.TryPeek(out var evt));
        Assert.NotNull(evt);
        Assert.Single(evt!.Added);
        Assert.Equal(MarkerKind.CargoShip, evt.Added[0].Kind);
        Assert.Empty(evt.Removed);
        Assert.Equal(expectedDims, evt.Dimensions);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try { await subTask; }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task Failed_marker_poll_retains_previous_snapshot()
    {
        // Contract: a thrown poll does not corrupt the previous snapshot.
        //
        // Script:
        //   Poll 1 → []          (baseline — no event)
        //   Poll 2 → [CargoShip] (added → exactly one event; queue empties, hold-last = CargoShip)
        //   Poll 3 → throws      (MarkersThrow = true; no event, snapshot retained)
        //   Poll 4 → [CargoShip] (same as held-last → no spurious diff, still exactly one event total)
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<MapMarkersChangedEvent>();
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in h.Bus.SubscribeAsync<MapMarkersChangedEvent>(cts.Token))
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);

        var cargo = new MapMarkerSnapshot(3UL, MarkerKind.CargoShip, 2f, 2f, null);
        // Script polls before EnsureConnectionAsync to eliminate the setup race.
        source.EnqueueMarkers([]); // poll 1: baseline
        source.EnqueueMarkers([cargo]); // poll 2: CargoShip added; queue empties → hold-last = [cargo]

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        // Wait for the one cargo-added event — definite signal that poll 2 completed.
        await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);
        Assert.Single(captured);

        // Poll 3: make the next poll throw; the poll loop catches the exception and retains the snapshot.
        source.LastConnection!.MarkersThrow = true;
        // Give enough time for at least one failing poll to be attempted.
        await Task.Delay(TimeSpan.FromMilliseconds(60), cts.Token);

        // Poll 4: recover — hold-last still returns [cargo], so snapshot is unchanged, no new event.
        source.LastConnection!.MarkersThrow = false;
        await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);

        // Still only one event total — the failed poll did not corrupt the snapshot.
        Assert.Single(captured);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try { await subTask; }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(10, ct);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required IUserDmSender Dm { get; init; }
        public required ConnectionSupervisor Supervisor { get; init; }
        public required IEventBus Bus { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Supervisor.StopAllAsync();
            await Provider.DisposeAsync();
        }
    }
}
