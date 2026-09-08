using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Connections;
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
    private static Harness CreateHarness(
        FakeRustSocketSource source,
        TimeSpan? teamPollInterval = null,
        Func<string, string>? unprotect = null,
        IEventBus? eventBus = null,
        TimeSpan? initialRetryDelay = null,
        TimeSpan? maxRetryDelay = null)
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Unprotect(Arg.Any<string>()).Returns(c => (unprotect ?? (token => token))(c.Arg<string>()));

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var dm = Substitute.For<IUserDmSender>();

        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddSingleton(clock);
        services.AddSingleton(protector);
        services.AddSingleton(dm);
        services.AddSingleton(eventBus ?? new InMemoryEventBus());

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
            InitialRetryDelay = initialRetryDelay ?? TimeSpan.FromMilliseconds(5),
            MaxRetryDelay = maxRetryDelay ?? TimeSpan.FromMilliseconds(20),
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatTimeout = TimeSpan.FromMilliseconds(200),
            MarkerPollInterval = TimeSpan.FromMilliseconds(20),
            MarkerPollFastInterval = TimeSpan.FromMilliseconds(20),
            LivenessPollInterval = TimeSpan.FromMilliseconds(20),
            TeamPollInterval = teamPollInterval ?? TimeSpan.FromMilliseconds(20),
        }));
        services.AddSingleton<ConnectionSecurity>();
        services.AddSingleton<ConnectionSupervisor>();

        var provider = services.BuildServiceProvider();
        return new Harness
        {
            Provider = provider,
            Dm = dm,
            Supervisor = provider.GetRequiredService<ConnectionSupervisor>(),
            Bus = provider.GetRequiredService<IEventBus>(),
            Logs = logs,
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
        Assert.Equal(7, state.PlayerCount);
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

    /// <summary>
    /// A per-request timeout in a connected window (here the map fetch the marker poll issues for oil-rig
    /// detection) surfaces as an <see cref="OperationCanceledException"/> even though the connection token is
    /// not cancelled. It must degrade the window — no rigs, no grid references, no base map — NOT terminate
    /// the whole connection loop, otherwise a transient slow map endpoint permanently kills the connection
    /// with no reconnect (the real-world bug).
    /// </summary>
    [Fact]
    public async Task Connect_MapFetchTimeout_DoesNotKillLoop_AndStillReconnects()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.TimeoutOnMapOnce(); // the window's map resolve times out on the FIRST connection only
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

    /// <summary>
    /// The Rust+ library raises no event when the SERVER closes the socket; only
    /// <see cref="IRustServerConnection.IsConnected"/> flips. The liveness watchdog must notice that and
    /// drive a reconnect promptly, without waiting for the (up to a minute apart) heartbeat — here the fake's
    /// heartbeat keeps reporting Ok, so only the watchdog can detect the drop.
    /// </summary>
    [Fact]
    public async Task ServerClosesSocket_LivenessWatchdog_DrivesReconnect()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(2)); // first heartbeat -> Connected; held thereafter
        source.EnqueueConnect(SocketConnectOutcome.Connected); // reconnect after the watchdog fires
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);
        var connected = await WaitForStateAsync(
            h.Provider, serverId, s => s.Status == ConnectionStatus.Connected && s.PlayerCount == 2);
        Assert.NotNull(connected);
        var live = source.LastConnection;
        Assert.NotNull(live);

        // Simulate a server-initiated close: the socket is no longer open, but the heartbeat still answers Ok.
        live.IsConnected = false;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (source.CreateCount < 2 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(15);
        }

        Assert.True(source.CreateCount >= 2, "the liveness watchdog should have driven a reconnect");
    }

    /// <summary>
    /// WasConnected must be computed from in-process state (the DB status survives restarts and
    /// would claim Connected at boot): statuses before the first Connected carry false; the drop
    /// after a Connected carries true.
    /// </summary>
    [Fact]
    public async Task StatusEvents_CarryWasConnected_OnlyAfterAConnectedDrop()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(2)); // first heartbeat -> Connected
        source.EnqueueHeartbeat(HeartbeatResult.Unreachable); // next heartbeat -> drop
        source.EnqueueConnect(SocketConnectOutcome.Connected); // reconnect
        source.EnqueueHeartbeat(HeartbeatResult.Ok(4));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var events = new List<ConnectionStatusChangedEvent>();
        // Register the subscription synchronously on this thread BEFORE any connection work runs.
        // InMemoryEventBus registers the channel eagerly when SubscribeAsync is invoked, so buffering
        // starts here. Deferring the call into the Task.Run below would race the supervisor's first
        // publishes: on a slow runner the initial Connecting/Connected events are dropped, the drop
        // event (WasConnected=true) becomes the collector's first observation, and the "before first
        // Connected" assertion fails.
        var stream = h.Bus.SubscribeAsync<ConnectionStatusChangedEvent>(cts.Token);
        _ = Task.Run(async () =>
        {
            await foreach (var e in stream)
            {
                lock (events)
                {
                    events.Add(e);
                }
            }
        }, cts.Token);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);
        var recovered = await WaitForStateAsync(
            h.Provider, serverId, s => s.Status == ConnectionStatus.Connected && s.PlayerCount == 4);
        Assert.NotNull(recovered);

        // The bus delivers asynchronously; wait until the collector has seen the drop.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (events)
            {
                if (events.Any(e => !e.IsConnected && e.WasConnected))
                {
                    break;
                }
            }

            await Task.Delay(15);
        }

        await cts.CancelAsync();
        ConnectionStatusChangedEvent[] snapshot;
        lock (events)
        {
            snapshot = [.. events];
        }

        var firstConnected = Array.FindIndex(snapshot, e => e.IsConnected);
        Assert.True(firstConnected >= 0, "expected a Connected status event");
        Assert.All(snapshot.Take(firstConnected), e => Assert.False(e.WasConnected));
        var drop = Array.FindIndex(
            snapshot, firstConnected, snapshot.Length - firstConnected, e => !e.IsConnected);
        Assert.True(drop > firstConnected, "expected a drop event after Connected");
        Assert.True(snapshot[drop].WasConnected);
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
        Assert.Single(evt.Added);
        Assert.Equal(MarkerKind.PatrolHelicopter, evt.Added[0].Kind);
        Assert.Empty(evt.Removed);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try
        {
            await subTask;
        }
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

        // FakeConnection defaults are GeometryResult new(4000u, 4000u, 500) + World size 4000u.
        var expectedDims = new MapDimensions(4000u, 4000u, 500, WorldSize: 4000u);

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
        Assert.Single(evt.Added);
        Assert.Equal(MarkerKind.CargoShip, evt.Added[0].Kind);
        Assert.Empty(evt.Removed);
        Assert.Equal(expectedDims, evt.Dimensions);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try
        {
            await subTask;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task PollMarkers_PublishesObservedVendingMachines()
    {
        // Contract: every marker poll publishes the server's complete vending-machine set — Rust
        // re-sends the whole bucket each poll, so this fires unconditionally (no change detection).
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<VendingMachinesObservedEvent>();
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in h.Bus.SubscribeAsync<VendingMachinesObservedEvent>(cts.Token))
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);

        // Pre-stage before EnsureConnectionAsync so the vending set is in place before the poll loop
        // starts — eliminates any setup race, matching the marker/monument staging convention.
        source.SetVendingMachines([
            new VendingMachineSnapshot(42UL, 100f, 200f, "Bob's Shop", false, [
                new VendingOfferSnapshot(69511070, false, 1, -932201673, false, 12, 5)
            ])
        ]);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        // Wait for the definite signal: the vending event arriving proves the poll ran.
        await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);

        Assert.True(captured.TryPeek(out var observed));
        Assert.NotNull(observed);
        Assert.Equal(10UL, observed.GuildId);
        Assert.Equal(serverId, observed.ServerId);

        // FakeConnection defaults give WorldSize 4000u (from World); assert WorldSize
        // threads through from dimensions to the published event.
        Assert.Equal(4000u, observed.WorldSize);
        var machine = Assert.Single(observed.Machines);
        Assert.Equal(42UL, machine.Id);
        Assert.Equal("Bob's Shop", machine.Name);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try
        {
            await subTask;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task Marker_position_change_publishes_moved_bucket()
    {
        // Contract: a marker present in consecutive polls whose position changed lands in Moved
        // (not Added/Removed), so the map can track cargo/heli movement.
        //
        // Script:
        //   Poll 1 → [Cargo id 7 @ (100, 100)]  (baseline — no event)
        //   Poll 2 → [Cargo id 7 @ (150, 130)]  (moved → one event)
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

        source.EnqueueMarkers([new MapMarkerSnapshot(7UL, MarkerKind.CargoShip, 100f, 100f, "Cargo A")]);
        source.EnqueueMarkers([new MapMarkerSnapshot(7UL, MarkerKind.CargoShip, 150f, 130f, "Cargo A")]);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);

        Assert.Single(captured);
        Assert.True(captured.TryPeek(out var evt));
        Assert.NotNull(evt);
        Assert.Empty(evt.Added);
        Assert.Empty(evt.Removed);
        var moved = Assert.Single(evt.Moved);
        Assert.Equal(7UL, moved.Id);
        Assert.Equal(150f, moved.X);
        Assert.Equal(130f, moved.Y);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try
        {
            await subTask;
        }
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
        source.LastConnection.MarkersThrow = false;
        await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);

        // Still only one event total — the failed poll did not corrupt the snapshot.
        Assert.Single(captured);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try
        {
            await subTask;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task Ch47_entering_rig_radius_publishes_activated_once_per_visit()
    {
        // Rig at (1000, 1000). Poll 1: CH47 far away (no event). Poll 2: CH47 within radius (Activated).
        // Poll 3: CH47 still within radius (no re-fire). Poll 4: CH47 gone (no event).
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        source.SetMonuments([new MonumentSnapshot("oil_rig_small", 1000f, 1000f)]);
        source.EnqueueMarkers([new MapMarkerSnapshot(1UL, MarkerKind.Chinook, 0f, 0f, null)]); // poll 1: far
        source.EnqueueMarkers([
            new MapMarkerSnapshot(1UL, MarkerKind.Chinook, 1010f, 1010f, null)
        ]); // poll 2: in radius
        source.EnqueueMarkers([new MapMarkerSnapshot(1UL, MarkerKind.Chinook, 1005f, 1005f, null)]); // poll 3: still in
        source.EnqueueMarkers([]); // poll 4: gone
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var rigEvents = new System.Collections.Concurrent.ConcurrentQueue<RigStateChangedEvent>();
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in h.Bus.SubscribeAsync<RigStateChangedEvent>(cts.Token))
            {
                rigEvents.Enqueue(e);
            }
        }, CancellationToken.None);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        // Wait for the Activated event — its arrival proves poll 2 completed.
        await WaitUntilAsync(() => !rigEvents.IsEmpty, cts.Token);

        // Give a little more time so poll 3 can complete (should NOT fire again).
        await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);

        Assert.Single(rigEvents);
        Assert.True(rigEvents.TryPeek(out var evt));
        Assert.NotNull(evt);
        Assert.Equal(RigKind.Small, evt.Rig);
        Assert.Equal(RigEventKind.Activated, evt.Kind);
        Assert.Equal(1000f, evt.X);
        Assert.Equal(1000f, evt.Y);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try
        {
            await subTask;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task Ch47_far_from_rig_publishes_chinook_event_but_no_rig_event()
    {
        // Rig at (1000, 1000). CH47 spawns far away at (0, 0) — no rig activation.
        // Expects: a MapMarkersChangedEvent with an Added Chinook, and zero RigStateChangedEvents.
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        source.SetMonuments([new MonumentSnapshot("oil_rig_small", 1000f, 1000f)]);
        source.EnqueueMarkers([]); // poll 1: baseline
        source.EnqueueMarkers([new MapMarkerSnapshot(1UL, MarkerKind.Chinook, 0f, 0f, null)]); // poll 2: CH47 far
        source.EnqueueMarkers([]); // poll 3: gone
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var markerEvents = new System.Collections.Concurrent.ConcurrentQueue<MapMarkersChangedEvent>();
        var rigEvents = new System.Collections.Concurrent.ConcurrentQueue<RigStateChangedEvent>();
        var markerSub = Task.Run(async () =>
        {
            await foreach (var e in h.Bus.SubscribeAsync<MapMarkersChangedEvent>(cts.Token))
            {
                markerEvents.Enqueue(e);
            }
        }, CancellationToken.None);
        var rigSub = Task.Run(async () =>
        {
            await foreach (var e in h.Bus.SubscribeAsync<RigStateChangedEvent>(cts.Token))
            {
                rigEvents.Enqueue(e);
            }
        }, CancellationToken.None);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        // Wait for the Chinook-added marker event — definite signal that poll 2 completed.
        await WaitUntilAsync(
            () => markerEvents.Any(e => e.Added.Any(m => m.Kind == MarkerKind.Chinook)), cts.Token);

        // Give a little more time so poll 3 can complete — still no rig event.
        await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);

        Assert.Contains(markerEvents, e => e.Added.Any(m => m.Kind == MarkerKind.Chinook));
        Assert.Empty(rigEvents);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try
        {
            await markerSub;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }

        try
        {
            await rigSub;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task TeamChanged_push_publishes_player_state_transition()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        // Make the slow poll inert: TeamResult = null means Diff(null) is a no-op, so the tracker's baseline
        // is driven ONLY by the pushed snapshots below — no race between the priming poll and the pushes.
        source.LastConnectionSetup = c => c.TeamResult = null;
        // Large team-poll interval too, belt-and-suspenders.
        await using var h = CreateHarness(source, teamPollInterval: TimeSpan.FromSeconds(30));
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<PlayerStateChangedEvent>();
        var stream = h.Bus.SubscribeAsync<PlayerStateChangedEvent>(cts.Token);
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in stream)
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        var conn = source.LastConnection!;

        var online = new TeamMemberSnapshot(
            100UL, "Alice", 1f, 1f, IsOnline: true, IsAlive: true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var offline = online with
        {
            IsOnline = false
        };

        conn.RaiseTeamChanged(new TeamInfoSnapshot(100UL, [online])); // prime (silent)
        conn.RaiseTeamChanged(new TeamInfoSnapshot(100UL, [offline])); // -> Disconnect

        await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);

        Assert.True(captured.TryDequeue(out var evt));
        var transition = Assert.Single(evt.Transitions);
        Assert.Equal(PlayerTransitionKind.Disconnect, transition.Kind);
        Assert.Equal(100UL, transition.SteamId);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try { await subTask; }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task TeamPoll_tick_publishes_transition_without_any_push()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        // Fast team poll so the tick drives the transition quickly.
        await using var h = CreateHarness(source, teamPollInterval: TimeSpan.FromMilliseconds(20));
        var (serverId, _, _) = await SeedAsync(h.Provider);

        var online = new TeamMemberSnapshot(
            100UL, "Alice", 1f, 1f, IsOnline: true, IsAlive: true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        source.LastConnectionSetup = c => c.TeamResult = new TeamInfoSnapshot(100UL, [online]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<PlayerStateChangedEvent>();
        var stream = h.Bus.SubscribeAsync<PlayerStateChangedEvent>(cts.Token);
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in stream)
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        var conn = source.LastConnection!;

        // Wait for at least one poll to prime the baseline with the online snapshot, then flip to offline.
        await WaitUntilAsync(() => conn.TeamInfoCallCount >= 1, cts.Token);
        conn.TeamResult = new TeamInfoSnapshot(100UL, [
            online with
            {
                IsOnline = false
            }
        ]);

        await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);
        Assert.True(captured.TryDequeue(out var evt));
        Assert.Contains(evt.Transitions, t => t.Kind == PlayerTransitionKind.Disconnect && t.SteamId == 100UL);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try { await subTask; }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task TeamInfo_is_not_polled_on_the_fast_marker_cadence()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        // Marker cadence stays fast (20ms from the harness); team poll is slow.
        await using var h = CreateHarness(source, teamPollInterval: TimeSpan.FromSeconds(10));
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        var conn = source.LastConnection!;

        // Wait for the team poll's immediate first (priming) call, then let ~10 marker intervals elapse.
        await WaitUntilAsync(() => conn.TeamInfoCallCount >= 1, cts.Token);
        await Task.Delay(200, cts.Token);

        // Only the single priming poll ran; the 10s team interval hasn't elapsed, so the fast marker
        // cadence did NOT trigger any further team-info reads.
        Assert.Equal(1, conn.TeamInfoCallCount);

        await h.Supervisor.StopAllAsync();
    }

    [Fact]
    public async Task TeamPoll_flags_afk_during_broadcast_silence()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        // A stationary, online, alive member — never moves, so it must become AFK once enough time passes.
        var online = new TeamMemberSnapshot(
            100UL, "Alice", 1f, 1f, IsOnline: true, IsAlive: true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        source.LastConnectionSetup = c => c.TeamResult = new TeamInfoSnapshot(100UL, [online]);
        // Fast tick so AFK is re-evaluated promptly once the clock advances. No team_changed push is ever raised.
        await using var h = CreateHarness(source, teamPollInterval: TimeSpan.FromMilliseconds(20));
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<PlayerStateChangedEvent>();
        var stream = h.Bus.SubscribeAsync<PlayerStateChangedEvent>(cts.Token);
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in stream)
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        var conn = source.LastConnection!;

        // Let the poll prime the baseline at t=epoch (member still, not yet AFK).
        await WaitUntilAsync(() => conn.TeamInfoCallCount >= 1, cts.Token);

        // Advance the fake clock past AfkThreshold (default 5m) WITHOUT any team_changed push.
        var clock = h.Provider.GetRequiredService<RustPlusBot.Abstractions.Time.IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(6));

        await WaitUntilAsync(
            () => captured.SelectMany(e => e.Transitions).Any(t => t.Kind == PlayerTransitionKind.BecameAfk),
            cts.Token);
        Assert.Contains(
            captured.SelectMany(e => e.Transitions),
            t => t.Kind == PlayerTransitionKind.BecameAfk && t.SteamId == 100UL);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try { await subTask; }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    /// <summary>
    /// The FIRST heartbeat of a connected window is the one that promotes the socket to Connected. When it
    /// comes back AuthRejected the window must be abandoned, the credential burned and the pool failed over —
    /// not left sitting in Connecting until a heartbeat that will never be accepted.
    /// </summary>
    [Fact]
    public async Task FirstHeartbeat_AuthRejected_FailsOverAndReconnects()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected); // credential A: socket opens...
        source.EnqueueHeartbeat(HeartbeatResult.AuthRejected); // ...but the very first heartbeat is rejected
        source.EnqueueConnect(SocketConnectOutcome.Connected); // credential B
        source.EnqueueHeartbeat(HeartbeatResult.Ok(5));
        await using var h = CreateHarness(source);
        var (serverId, credA, credB) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(
            h.Provider, serverId, s => s.Status == ConnectionStatus.Connected && s.PlayerCount == 5);
        Assert.NotNull(state);
        Assert.Equal(CredentialStatus.Invalid, await CredStatusAsync(h.Provider, credA));
        Assert.Equal(CredentialStatus.Active, await CredStatusAsync(h.Provider, credB));
        await h.Dm.Received(1).SendAsync(1UL, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unreachable FIRST heartbeat is a transport problem, not a credential problem: the loop must back
    /// off and retry the SAME credential. Burning it here would walk a healthy pool to NoCredentials during
    /// a server restart.
    /// </summary>
    [Fact]
    public async Task FirstHeartbeat_Unreachable_RetriesWithoutBurningTheCredential()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Unreachable); // first heartbeat of the window fails
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(6));
        await using var h = CreateHarness(source);
        var (serverId, credA, credB) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(
            h.Provider, serverId, s => s.Status == ConnectionStatus.Connected && s.PlayerCount == 6);
        Assert.NotNull(state);
        Assert.True(source.CreateCount >= 2, "the loop should have reconnected after the failed first heartbeat");
        Assert.Equal(CredentialStatus.Active, await CredStatusAsync(h.Provider, credA));
        Assert.Equal(CredentialStatus.Standby, await CredStatusAsync(h.Provider, credB));
        await h.Dm.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A credential can be revoked in-game while the socket is up. The periodic heartbeat is what notices,
    /// and it must drive the same failover as a rejected connect.
    /// </summary>
    [Fact]
    public async Task Heartbeat_AuthRejected_MidWindow_FailsOverAndReconnects()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(2)); // window goes live
        source.EnqueueHeartbeat(HeartbeatResult.AuthRejected); // then the credential is revoked in-game
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(8));
        await using var h = CreateHarness(source);
        var (serverId, credA, credB) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(
            h.Provider, serverId, s => s.Status == ConnectionStatus.Connected && s.PlayerCount == 8);
        Assert.NotNull(state);
        Assert.Equal(CredentialStatus.Invalid, await CredStatusAsync(h.Provider, credA));
        Assert.Equal(CredentialStatus.Active, await CredStatusAsync(h.Provider, credB));
        Assert.Equal(200UL, source.LastSteamId);
    }

    /// <summary>
    /// A socket library that throws while connecting (rather than reporting an outcome) must not take the
    /// host down, and must leave the supervisor able to start the server again. The loop itself ends — that
    /// is the documented contract of the outer catch — so the regression this pins is that the failure is
    /// LOGGED and CONTAINED rather than silently swallowed or propagated.
    /// </summary>
    [Fact]
    public async Task Faulting_connect_is_logged_and_leaves_the_supervisor_restartable()
    {
        var source = new FakeRustSocketSource();
        source.LastConnectionSetup = c =>
        {
            if (source.CreateCount == 1)
            {
                c.ConnectFault = new InvalidOperationException("socket library faulted");
            }
        };
        source.EnqueueHeartbeat(HeartbeatResult.Ok(9));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitForLogAsync(h, LogLevel.Error, "faulted", cts.Token);

        // The second attempt gets a healthy socket: nothing about the fault is sticky.
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        var state = await WaitForStateAsync(
            h.Provider, serverId, s => s.Status == ConnectionStatus.Connected && s.PlayerCount == 9);
        Assert.NotNull(state);
    }

    /// <summary>
    /// Stopping a server whose connect is still in flight must complete and must dispose the half-open
    /// socket. A leaked socket here accumulates one live WebSocket per stop/start cycle.
    /// </summary>
    [Fact]
    public async Task Stop_while_connecting_disposes_the_socket_and_returns()
    {
        var source = new FakeRustSocketSource
        {
            LastConnectionSetup = c => c.BlockConnectUntilCancelled = true
        };
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => source.LastConnection is not null, cts.Token);
        var connecting = source.LastConnection!;

        await h.Supervisor.StopAsync(10UL, serverId).WaitAsync(TimeSpan.FromSeconds(30), cts.Token);

        Assert.Equal(1, connecting.DisposeCount);
        Assert.False(h.Supervisor.HasLiveSocket(10UL, serverId));
    }

    /// <summary>
    /// A stored token that no longer decrypts (key rotation, corrupted blob) must burn that credential, tell
    /// its owner why, and move on to the next one in the pool — the loop must not spin on it forever.
    /// </summary>
    [Fact]
    public async Task Unreadable_token_invalidates_the_credential_dms_the_owner_and_fails_over()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(3));
        await using var h = CreateHarness(
            source,
            unprotect: token => token == "111" ? throw new CryptographicException("key rotated") : token);
        var (serverId, credA, credB) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        Assert.Equal(CredentialStatus.Invalid, await CredStatusAsync(h.Provider, credA));
        Assert.Equal(CredentialStatus.Active, await CredStatusAsync(h.Provider, credB));
        Assert.Equal(200UL, source.LastSteamId);
        await h.Dm.Received(1).SendAsync(
            1UL,
            Arg.Is<string>(m => m.Contains("could not be read", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// After <see cref="ConnectionSupervisor.StopAllAsync"/> the process is shutting down: a late
    /// EnsureConnection (a slash command racing shutdown, say) must not resurrect a loop that nothing will
    /// ever stop. The guard runs before any loop is scheduled, so this assertion is not timing-dependent.
    /// </summary>
    [Fact]
    public async Task EnsureConnection_after_StopAll_starts_no_loop()
    {
        var source = new FakeRustSocketSource();
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        await h.Supervisor.StopAllAsync();
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        Assert.Equal(0, source.CreateCount);
        Assert.False(h.Supervisor.HasLiveSocket(10UL, serverId));
    }

    /// <summary>
    /// Every inbound socket callback publishes through a guard that drops the event once the supervisor is
    /// disposed. Without it a callback racing shutdown publishes onto a bus whose consumers are gone, or
    /// touches an already-disposed shutdown token.
    /// </summary>
    [Fact]
    public async Task Nothing_is_published_once_the_supervisor_is_disposed()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Park the team poll inside its request, deliberately ignoring cancellation. The connected window
        // joins that poll before it detaches the socket handlers, so the handlers stay attached while the
        // supervisor is already disposed — exactly the race the guards exist for.
        source.LastConnectionSetup = c => c.TeamInfoHold = hold.Task;

        // Not `await using var h`: this test disposes the supervisor itself, and Harness.DisposeAsync would
        // then call StopAllAsync on an already-disposed CancellationTokenSource.
        var h = CreateHarness(source);
        await using var provider = h.Provider;
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var teamMessages = new System.Collections.Concurrent.ConcurrentQueue<TeamMessageReceivedEvent>();
        var deviceTriggers = new System.Collections.Concurrent.ConcurrentQueue<SmartDeviceTriggeredEvent>();
        var teamStream = h.Bus.SubscribeAsync<TeamMessageReceivedEvent>(cts.Token);
        var deviceStream = h.Bus.SubscribeAsync<SmartDeviceTriggeredEvent>(cts.Token);
        _ = Task.Run(
            async () =>
            {
                await foreach (var e in teamStream)
                {
                    teamMessages.Enqueue(e);
                }
            },
            CancellationToken.None);
        _ = Task.Run(
            async () =>
            {
                await foreach (var e in deviceStream)
                {
                    deviceTriggers.Enqueue(e);
                }
            },
            CancellationToken.None);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => h.Supervisor.HasLiveSocket(10UL, serverId), cts.Token);
        var conn = source.LastConnection!;
        await conn.TeamInfoEntered.WaitAsync(cts.Token);

        // DisposeAsync flips the guard before its first await, so it is already set when the raises below run.
        var disposeTask = h.Supervisor.DisposeAsync().AsTask();
        conn.RaiseTeamMessage(new TeamChatLine(100UL, "Alice", "after-dispose"));
        conn.RaiseClanMessage(new ClanChatLine(100UL, "Alice", "after-dispose", DateTimeOffset.UnixEpoch));
        conn.RaiseClanChanged(ClanProbeResult.NoClan);
        conn.RaiseTeamChanged(new TeamInfoSnapshot(100UL, []));
        conn.RaiseSmartDeviceTriggered(42UL, isActive: true);
        conn.RaiseStorageMonitorTriggered(43UL, new StorageContentsSnapshot(null, null, null, []));
        hold.SetResult();
        await disposeTask;

        // Barrier, not a sleep: the bus preserves publish order per subscription, so once a sentinel
        // published AFTER the raises has arrived, anything the raises published would have arrived first.
        await h.Bus.PublishAsync(
            new TeamMessageReceivedEvent(10UL, serverId, 1UL, "s", "sentinel", false), cts.Token);
        await h.Bus.PublishAsync(new SmartDeviceTriggeredEvent(10UL, serverId, 999UL, false), cts.Token);
        await WaitUntilAsync(
            () => teamMessages.Any(e => e.Message == "sentinel") && deviceTriggers.Any(e => e.EntityId == 999UL),
            cts.Token);

        Assert.DoesNotContain(teamMessages, e => e.Message == "after-dispose");
        Assert.DoesNotContain(deviceTriggers, e => e.EntityId == 42UL);
        await cts.CancelAsync();
    }

    /// <summary>
    /// A failing team poll must be logged and retried, never allowed to escape: an escaping exception ends
    /// the poll for the rest of the connection, and AFK detection dies silently until the bot restarts.
    /// </summary>
    [Fact]
    public async Task Team_poll_survives_repeated_failures()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        source.LastConnectionSetup = c => c.TeamInfoFault = new InvalidOperationException("team poll failed");
        await using var h = CreateHarness(source, teamPollInterval: TimeSpan.FromMilliseconds(20));
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => h.Supervisor.HasLiveSocket(10UL, serverId), cts.Token);
        var conn = source.LastConnection!;

        // Three attempts: the loop kept going after the first two throws instead of dying on them.
        await WaitUntilAsync(() => conn.TeamInfoCallCount >= 3, cts.Token);
        await WaitForLogAsync(h, LogLevel.Warning, "Team poll", cts.Token);
        Assert.True(h.Supervisor.HasLiveSocket(10UL, serverId), "the connection must survive a failing poll");

        await h.Supervisor.StopAllAsync();
    }

    /// <summary>
    /// A team poll parked in a request when the window is torn down must unwind on cancellation instead of
    /// wedging teardown — a stuck teardown blocks the whole supervisor's shutdown gate.
    /// </summary>
    [Fact]
    public async Task Team_poll_parked_in_a_request_does_not_wedge_teardown()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        source.LastConnectionSetup = c => c.BlockTeamInfoUntilCancelled = true;
        await using var h = CreateHarness(source, teamPollInterval: TimeSpan.FromMilliseconds(20));
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => h.Supervisor.HasLiveSocket(10UL, serverId), cts.Token);
        var conn = source.LastConnection;
        Assert.NotNull(conn);
        await conn.TeamInfoEntered.WaitAsync(cts.Token);

        await h.Supervisor.StopAllAsync().WaitAsync(TimeSpan.FromSeconds(30), cts.Token);

        Assert.False(h.Supervisor.HasLiveSocket(10UL, serverId));
    }

    /// <summary>
    /// Every read seam answers its "not connected" default rather than throwing, so a command issued against
    /// a disconnected server degrades instead of faulting the Discord interaction handling it.
    /// </summary>
    [Fact]
    public async Task Every_read_seam_degrades_to_its_default_without_a_live_socket()
    {
        var source = new FakeRustSocketSource();
        await using var h = CreateHarness(source);
        var unknown = Guid.NewGuid();

        // Null, not an empty list: callers must be able to tell "not connected" from "nobody is AFK".
        Assert.Null(await h.Supervisor.GetAfkMembersAsync(10UL, unknown, CancellationToken.None));
        Assert.Null(await h.Supervisor.GetWorldAsync(10UL, unknown, CancellationToken.None));
        Assert.Null(await h.Supervisor.GetMapDimensionsAsync(10UL, unknown, CancellationToken.None));
        Assert.Null(await h.Supervisor.GetSmartSwitchStateAsync(10UL, unknown, 1UL, CancellationToken.None));
        Assert.Equal(
            DeviceReachability.NoResponse,
            await h.Supervisor.StrobeSmartSwitchAsync(10UL, unknown, 1UL, 100, value: true, CancellationToken.None));
        Assert.False(await h.Supervisor.SetClanMotdAsync(10UL, unknown, "motd", CancellationToken.None));
        Assert.Equal(0, source.CreateCount);
    }

    /// <summary>Each read seam is wired to the live window's socket, not to a stale or default value.</summary>
    [Fact]
    public async Task Every_read_seam_answers_from_the_live_socket()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        source.StageDeviceState(50UL, isActive: true);
        source.LastConnectionSetup = c =>
        {
            c.World = new WorldSnapshot(3500u, 7u);
            c.StrobeSwitchReachability = DeviceReachability.NoPrivilege;
        };
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => h.Supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        var world = await h.Supervisor.GetWorldAsync(10UL, serverId, cts.Token);
        Assert.NotNull(world);
        Assert.Equal(3500u, world.WorldSize);
        Assert.True(await h.Supervisor.GetSmartSwitchStateAsync(10UL, serverId, 50UL, cts.Token));
        Assert.Equal(
            DeviceReachability.NoPrivilege,
            await h.Supervisor.StrobeSmartSwitchAsync(10UL, serverId, 50UL, 100, value: true, cts.Token));
        Assert.True(await h.Supervisor.SetClanMotdAsync(10UL, serverId, "motd", cts.Token));

        await h.Supervisor.StopAllAsync();
    }

    /// <summary>
    /// The map seams swallow failures on purpose (a render must degrade, never fault the consuming loop) —
    /// but a CALLER's cancellation is a shutdown signal and must still propagate, otherwise a shutting-down
    /// caller silently gets "no map" and carries on.
    /// </summary>
    [Fact]
    public async Task Map_seams_propagate_the_callers_cancellation_instead_of_degrading()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        // The window's map never resolves, so each read reaches the cache's gate, where the caller's
        // already-cancelled token is observed.
        source.LastConnectionSetup = c => c.MapFault = new InvalidOperationException("GetMap returned no data");
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => h.Supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Supervisor.GetMapImageAsync(10UL, serverId, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Supervisor.GetMapDimensionsAsync(10UL, serverId, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Supervisor.GetMonumentsAsync(10UL, serverId, cancelled.Token));

        await h.Supervisor.StopAllAsync();
    }

    /// <summary>
    /// A relay send must be bounded by the caller's token: an in-game send whose reply never arrives would
    /// otherwise park the calling relay loop forever.
    /// </summary>
    [Fact]
    public async Task Send_propagates_the_callers_cancellation_when_the_reply_never_arrives()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        source.LastConnectionSetup = c => c.HangOnSend = true;
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => h.Supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Supervisor.SendAsync(ChatChannelKind.Team, 10UL, serverId, "hi", cancelled.Token));

        // An unroutable channel is a caller bug, not a transport failure: report it, do not throw.
        Assert.Equal(
            ChatSendResult.Failed,
            await h.Supervisor.SendAsync((ChatChannelKind)99, 10UL, serverId, "hi", cts.Token));

        await h.Supervisor.StopAllAsync();
    }

    /// <summary>
    /// Rig detection keys off oil-rig monuments and CH47 markers only. A non-CH47 marker parked ON a rig
    /// and an unrecognised monument token must both be ignored, or every cargo ship passing an oil rig
    /// would ping the guild.
    /// </summary>
    /// <remarks>
    /// The script separates the two rigs so the guard is observable in the event ORDER, which the bus
    /// preserves within a subscription:
    /// <list type="bullet">
    /// <item>poll 1 — empty baseline.</item>
    /// <item>poll 2 — a cargo ship parked on the SMALL rig. Must publish nothing.</item>
    /// <item>poll 3 — a CH47 on the LARGE rig. Must publish Activated(Large).</item>
    /// <item>poll 4 — a CH47 on the SMALL rig. Publishes Activated(Small); this is the barrier.</item>
    /// </list>
    /// Waiting for the barrier event guarantees every earlier publish has already been delivered, so the
    /// expected sequence is exactly [Large, Small]. Drop the CH47 guard and poll 2's cargo ship activates
    /// the small rig first, making the sequence [Small, Large, …] — the assertion fails on ORDER, never on
    /// a race.
    /// </remarks>
    [Fact]
    public async Task Non_rig_monuments_and_non_chinook_markers_never_activate_a_rig()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        source.SetMonuments([
            new MonumentSnapshot("lighthouse", 1000f, 1000f), // not a rig: must never produce an event
            new MonumentSnapshot("oil_rig_small", 1000f, 1000f),
            new MonumentSnapshot("large_oil_rig", 5000f, 5000f),
        ]);
        source.EnqueueMarkers([]); // poll 1: baseline
        source.EnqueueMarkers([new MapMarkerSnapshot(1UL, MarkerKind.CargoShip, 1000f, 1000f, null)]); // poll 2
        source.EnqueueMarkers([new MapMarkerSnapshot(2UL, MarkerKind.Chinook, 5000f, 5000f, null)]); // poll 3
        source.EnqueueMarkers([new MapMarkerSnapshot(3UL, MarkerKind.Chinook, 1000f, 1000f, null)]); // poll 4
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var rigEvents = new System.Collections.Concurrent.ConcurrentQueue<RigStateChangedEvent>();
        var subTask = Task.Run(
            async () =>
            {
                await foreach (var e in h.Bus.SubscribeAsync<RigStateChangedEvent>(cts.Token))
                {
                    rigEvents.Enqueue(e);
                }
            },
            CancellationToken.None);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        // Barrier: the small-rig activation is published last, so once two events have arrived every
        // earlier publish has been delivered too — a spurious one cannot merely be "not yet observed".
        await WaitUntilAsync(() => rigEvents.Count >= 2, cts.Token);

        RigKind[] expected = [RigKind.Large, RigKind.Small];
        RigKind[] observed = [.. rigEvents.Select(e => e.Rig)];
        Assert.Equal(expected, observed);
        Assert.All(rigEvents, e => Assert.Equal(RigEventKind.Activated, e.Kind));

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try
        {
            await subTask;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    /// <summary>
    /// The AFK seam must read the LIVE window's tracker: after a reconnect the old tracker is gone, so a
    /// seam bound to a stale one reports AFK state from a window that no longer exists.
    /// </summary>
    [Fact]
    public async Task GetAfkMembers_reports_the_live_windows_tracker()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        var still = new TeamMemberSnapshot(
            100UL, "Alice", 1f, 1f, IsOnline: true, IsAlive: true, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        source.LastConnectionSetup = c => c.TeamResult = new TeamInfoSnapshot(100UL, [still]);
        await using var h = CreateHarness(source, teamPollInterval: TimeSpan.FromMilliseconds(20));
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => h.Supervisor.HasLiveSocket(10UL, serverId), cts.Token);
        var conn = source.LastConnection;
        Assert.NotNull(conn);
        await conn.TeamInfoEntered.WaitAsync(cts.Token);

        // Advance past AfkThreshold; the team poll re-runs the diff on the tracker the seam reads.
        var clock = h.Provider.GetRequiredService<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(6));

        IReadOnlyList<AfkMember>? afk;
        while (true)
        {
            afk = await h.Supervisor.GetAfkMembersAsync(10UL, serverId, cts.Token);
            if (afk is { Count: > 0 })
            {
                break;
            }

            await Task.Delay(10, cts.Token);
        }

        var member = Assert.Single(afk);
        Assert.Equal(100UL, member.SteamId);
        Assert.Equal(TimeSpan.FromMinutes(6), member.StillFor);

        await h.Supervisor.StopAllAsync();
    }

    /// <summary>
    /// The inbound socket callbacks are fire-and-forget. A publish that throws — a consumer bug, a bus
    /// backed by a failing transport — must be logged and swallowed inside each publisher: an escaping
    /// exception there becomes an unobserved task fault and, historically, a dead feature until restart.
    /// </summary>
    [Fact]
    public async Task A_failing_bus_publish_is_logged_and_never_escapes_a_socket_callback()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        var online = new TeamMemberSnapshot(
            100UL, "Alice", 1f, 1f, IsOnline: true, IsAlive: true, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        source.LastConnectionSetup = c => c.TeamResult = new TeamInfoSnapshot(100UL, [online]);
        // Status events keep working — they drive the connect loop itself; only the callback publishes fail.
        var bus = new FaultingEventBus(
            t => t != typeof(ConnectionStatusChangedEvent),
            () => new InvalidOperationException("bus refused the event"));
        await using var h = CreateHarness(source, teamPollInterval: TimeSpan.FromMilliseconds(20), eventBus: bus);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => h.Supervisor.HasLiveSocket(10UL, serverId), cts.Token);
        var conn = source.LastConnection;
        Assert.NotNull(conn);

        // Each publisher completes synchronously on the failing bus, so the log is in place once the raise
        // returns — no sleeping, no polling.
        // The second poll proves the first one primed the tracker's baseline, so the push below is a real
        // change (Alice goes offline) and therefore does publish.
        await WaitUntilAsync(() => conn.TeamInfoCallCount >= 2, cts.Token);

        conn.RaiseTeamMessage(new TeamChatLine(100UL, "Alice", "hi"));
        conn.RaiseClanMessage(new ClanChatLine(100UL, "Alice", "hi", DateTimeOffset.UnixEpoch));
        conn.RaiseSmartDeviceTriggered(42UL, isActive: true);
        conn.RaiseStorageMonitorTriggered(43UL, new StorageContentsSnapshot(null, null, null, []));
        conn.RaiseTeamChanged(new TeamInfoSnapshot(100UL, [
            online with
            {
                IsOnline = false
            }
        ]));

        Assert.Contains(h.Logs.Records, r => r.Message.Contains("received team message", StringComparison.Ordinal));
        Assert.Contains(h.Logs.Records, r => r.Message.Contains("a clan message", StringComparison.Ordinal));
        Assert.Contains(h.Logs.Records, r => r.Message.Contains("smart-device state", StringComparison.Ordinal));
        Assert.Contains(h.Logs.Records, r => r.Message.Contains("Publishing team state", StringComparison.Ordinal));

        // The connect-time clan probe publishes from the connect path, so that one is awaited.
        await WaitForLogAsync(h, LogLevel.Error, "Publishing clan state", cts.Token);

        // The window is unharmed: every failure stayed inside its publisher.
        Assert.True(h.Supervisor.HasLiveSocket(10UL, serverId));
        await h.Supervisor.StopAllAsync();
    }

    /// <summary>
    /// A publish cancelled because the process is shutting down is NOT a failure: swallowing it silently is
    /// the point, so a normal shutdown does not fill the log with false errors.
    /// </summary>
    [Fact]
    public async Task A_publish_cancelled_by_shutdown_is_swallowed_without_an_error()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        var bus = new FaultingEventBus(
            t => t != typeof(ConnectionStatusChangedEvent),
            () => new OperationCanceledException("shutting down"));
        await using var h = CreateHarness(source, eventBus: bus);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => h.Supervisor.HasLiveSocket(10UL, serverId), cts.Token);
        var conn = source.LastConnection;
        Assert.NotNull(conn);

        conn.RaiseTeamMessage(new TeamChatLine(100UL, "Alice", "hi"));
        conn.RaiseClanMessage(new ClanChatLine(100UL, "Alice", "hi", DateTimeOffset.UnixEpoch));
        conn.RaiseClanChanged(ClanProbeResult.NoClan);
        conn.RaiseTeamChanged(new TeamInfoSnapshot(100UL, []));
        conn.RaiseSmartDeviceTriggered(42UL, isActive: true);
        conn.RaiseStorageMonitorTriggered(43UL, new StorageContentsSnapshot(null, null, null, []));

        // Deterministic: each publisher runs to completion synchronously on this bus.
        Assert.DoesNotContain(h.Logs.Records, r => r.Message.Contains("Publishing a", StringComparison.Ordinal));
        Assert.True(h.Supervisor.HasLiveSocket(10UL, serverId));
        await h.Supervisor.StopAllAsync();
    }

    /// <summary>
    /// Repeated unreachable connects must back off and then STOP growing at MaxRetryDelay. An uncapped
    /// doubling walks a temporarily-down server out to hours between attempts, so it never comes back on
    /// its own — the delay must saturate instead.
    /// </summary>
    /// <remarks>
    /// With Initial=100ms and Max=400ms the intervals are 100, 200, 400, 400, 400: the 4th→5th and 5th→6th
    /// gaps are both the cap. Uncapped they would be 800 and 1600, so comparing those two gaps to each
    /// other — rather than to an absolute wall-clock budget — separates the two behaviours by 800ms while
    /// staying immune to a uniformly slow runner: scheduling jitter inflates both gaps, doubling does not.
    /// </remarks>
    [Fact]
    public async Task Reconnect_backoff_stops_growing_at_the_configured_cap()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Unreachable);
        source.EnqueueConnect(SocketConnectOutcome.Unreachable);
        source.EnqueueConnect(SocketConnectOutcome.Unreachable);
        source.EnqueueConnect(SocketConnectOutcome.Unreachable);
        source.EnqueueConnect(SocketConnectOutcome.Unreachable);
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(4));
        await using var h = CreateHarness(
            source,
            initialRetryDelay: TimeSpan.FromMilliseconds(100),
            maxRetryDelay: TimeSpan.FromMilliseconds(400));
        var (serverId, credA, _) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(
            h.Provider, serverId, s => s.Status == ConnectionStatus.Connected && s.PlayerCount == 4);
        Assert.NotNull(state);
        // Unreachable is a transport problem: the credential must survive all of it.
        Assert.Equal(CredentialStatus.Active, await CredStatusAsync(h.Provider, credA));

        var attempts = source.CreateTimestamps.ToArray();
        Assert.True(attempts.Length >= 6, $"expected 6 connect attempts, saw {attempts.Length}");
        var beforeCap = Stopwatch.GetElapsedTime(attempts[3], attempts[4]);
        var atCap = Stopwatch.GetElapsedTime(attempts[4], attempts[5]);

        // Task.Delay never fires early, so both gaps are at least the cap; this pins that the backoff had
        // actually reached it rather than still ramping up.
        Assert.True(
            beforeCap >= TimeSpan.FromMilliseconds(350),
            $"attempt 4->5 should have waited the {400}ms cap, waited {beforeCap.TotalMilliseconds:F0}ms");

        // The load-bearing assertion: the next gap must NOT have doubled. 250ms of slack absorbs scheduler
        // jitter; an uncapped backoff would be 800ms longer, far outside it.
        Assert.True(
            atCap <= beforeCap + TimeSpan.FromMilliseconds(250),
            $"backoff kept growing past the cap: {beforeCap.TotalMilliseconds:F0}ms then "
            + $"{atCap.TotalMilliseconds:F0}ms");
    }

    private static Task WaitForLogAsync(Harness h, LogLevel level, string fragment, CancellationToken ct) =>
        WaitUntilAsync(
            () => h.Logs.Records.Any(r => r.Level == level && r.Message.Contains(fragment, StringComparison.Ordinal)),
            ct);

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
        public required CapturingLoggerProvider Logs { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Supervisor.StopAllAsync();
            await Provider.DisposeAsync();
        }
    }
}
