using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Persistord.Testing;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Connections.Tests.Fakes;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Alarms;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.StorageMonitors;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class AlarmSweepTests
{
    private static (ServiceProvider Provider, ConnectionSupervisor Supervisor, InMemoryEventBus Bus) CreateHarness(
        FakeRustSocketSource source)
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Unprotect(Arg.Any<string>()).Returns(c => c.Arg<string>());
        var dm = Substitute.For<IUserDmSender>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var bus = new InMemoryEventBus();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton(protector);
        services.AddSingleton(dm);
        services.AddSingleton<IEventBus>(bus);

        // One shared-cache in-memory database several connections can open independently, so a
        // background loop and the test never run concurrent commands on one connection. Creating
        // the first context is what applies the migrations.
        var database = SqliteTestDatabase.Shared();
        var cs = database.ConnectionString;
        database.CreateContext<BotDbContext>(options => new BotDbContext(options)).Dispose();

        services.AddSingleton(database);
        services.AddScoped(_ => new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options));
        services.AddScoped<IConnectionStore, ConnectionStore>();
        services.AddScoped<IServerService, ServerService>();
        services.AddScoped<ISwitchStore, SwitchStore>();
        services.AddScoped<IAlarmStore, AlarmStore>();
        services.AddScoped<IStorageMonitorStore, StorageMonitorStore>();
        services.AddSingleton<IRustSocketSource>(source);
        services.AddSingleton(Options.Create(new ConnectionOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(1),
            InitialRetryDelay = TimeSpan.FromMilliseconds(5),
            MaxRetryDelay = TimeSpan.FromMilliseconds(20),
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatTimeout = TimeSpan.FromMilliseconds(200),
            ReachabilityPollInterval = TimeSpan.FromMilliseconds(25),
        }));
        services.AddSingleton<ConnectionSecurity>();
        services.AddSingleton<ConnectionSupervisor>();

        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<ConnectionSupervisor>(), bus);
    }

    private static async Task<Guid> SeedServerWithActiveAndAlarmAsync(ServiceProvider provider, ulong entityId)
    {
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        ctx.RustServers.Add(server);
        ctx.PlayerCredentials.Add(new PlayerCredential
        {
            GuildId = 10UL,
            RustServerId = server.Id,
            OwnerUserId = 1UL,
            SteamId = 555UL,
            ProtectedPlayerToken = "123",
            Status = CredentialStatus.Active,
        });
        await ctx.SaveChangesAsync();
        var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
        await store.AddAsync(10UL, server.Id, entityId, $"Alarm {entityId}", 1UL);
        return server.Id;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(10, ct);
        }
    }

    [Fact]
    public async Task GetSmartAlarmReading_reads_with_alarm_kind_when_connected()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, _) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndAlarmAsync(provider, entityId: 77UL);
        source.StageDeviceState(77UL, isActive: true);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        var reading = await supervisor.GetSmartAlarmReadingAsync(10UL, serverId, 77UL, cts.Token);

        Assert.Equal(DeviceReachability.Reachable, reading.Reachability);
        Assert.True(reading.IsActive);
        var connection = Assert.IsType<FakeRustSocketSource.FakeConnection>(source.LastConnection);
        lock (connection.DeviceReadCalls)
        {
            // The Rust+ API type-checks reads: the refresh path must read the alarm AS an alarm.
            Assert.Contains((77UL, SmartDeviceKind.Alarm), connection.DeviceReadCalls);
        }

        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task GetSmartAlarmReading_returns_noresponse_when_no_live_socket()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, _) = CreateHarness(source);
        await using var disposeProvider = provider;

        var reading = await supervisor.GetSmartAlarmReadingAsync(
            10UL, Guid.NewGuid(), 77UL, CancellationToken.None);

        Assert.Null(reading.IsActive);
        Assert.Equal(DeviceReachability.NoResponse, reading.Reachability);
    }

    [Fact]
    public async Task Sweep_publishes_observed_state_for_reachable_alarm()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndAlarmAsync(provider, entityId: 77UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Subscribe BEFORE connecting. The periodic sweep must publish the alarm state it already reads
        // as an OBSERVED event so drifted embeds self-correct without ping/relay side effects.
        var stream = bus.SubscribeAsync<SmartDeviceStateObservedEvent>(cts.Token);
        var received =
            new TaskCompletionSource<SmartDeviceStateObservedEvent>(TaskCreationOptions
                .RunContinuationsAsynchronously);
        _ = Task.Run(
            async () =>
            {
                await foreach (var evt in stream)
                {
                    if (evt.EntityId == 77UL && evt.IsActive)
                    {
                        received.TrySetResult(evt);
                        break;
                    }
                }
            },
            cts.Token);

        // The alarm reads as active; staged so it is in place before the connection is created.
        source.StageDeviceState(77UL, isActive: true);
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        var evt = await received.Task.WaitAsync(cts.Token);
        Assert.Equal(77UL, evt.EntityId);
        Assert.True(evt.IsActive);
        await supervisor.StopAllAsync();
    }

    /// <summary>
    /// The periodic sweep is the only thing that notices a device going away while the socket stays up —
    /// picked up, destroyed, TC privilege lost. The connect-time prime cannot see it, so without the sweep
    /// the embed keeps claiming the device is reachable until the next reconnect.
    /// </summary>
    [Fact]
    public async Task Sweep_publishes_a_reachability_change_that_happens_mid_window()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndAlarmAsync(provider, entityId: 77UL);
        source.StageDeviceState(77UL, isActive: true);
        // Stage the key up front so the flip below only rewrites an existing entry rather than growing the
        // dictionary the sweep is reading.
        source.StageDeviceReachability(77UL, DeviceReachability.Reachable);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var changes = new ConcurrentQueue<DeviceReachabilityChangedEvent>();
        var stream = bus.SubscribeAsync<DeviceReachabilityChangedEvent>(cts.Token);
        _ = Task.Run(
            async () =>
            {
                await foreach (var e in stream)
                {
                    changes.Enqueue(e);
                }
            },
            CancellationToken.None);

        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);
        var conn = source.LastConnection;
        Assert.NotNull(conn);

        // Prime plus two sweep cycles: the sweep's silent baseline is definitely seeded before the change.
        await WaitUntilAsync(() => ReadCount(conn, 77UL) >= 3, cts.Token);
        conn.DeviceReachabilityOverrides[77UL] = DeviceReachability.Removed;

        await WaitUntilAsync(
            () => changes.Any(e => e.EntityId == 77UL && e.Reachability == DeviceReachability.Removed),
            cts.Token);

        await supervisor.StopAllAsync();
        await cts.CancelAsync();
    }

    /// <summary>
    /// A failing sweep cycle must be logged and retried on the next tick. Letting the exception escape ends
    /// the sweep for the rest of the connection, so reachability changes go unreported until a restart —
    /// the silent-death failure mode this bot has actually shipped.
    /// </summary>
    [Fact]
    public async Task A_failing_sweep_cycle_is_retried_and_the_sweep_recovers()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndAlarmAsync(provider, entityId: 88UL);
        source.StageDeviceState(88UL, isActive: true);
        source.StageDeviceReachability(88UL, DeviceReachability.Reachable);
        source.LastConnectionSetup = c => c.DeviceInfoFault = new InvalidOperationException("device read failed");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var changes = new ConcurrentQueue<DeviceReachabilityChangedEvent>();
        var stream = bus.SubscribeAsync<DeviceReachabilityChangedEvent>(cts.Token);
        _ = Task.Run(
            async () =>
            {
                await foreach (var e in stream)
                {
                    changes.Enqueue(e);
                }
            },
            CancellationToken.None);

        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);
        var conn = source.LastConnection;
        Assert.NotNull(conn);

        // Every read throws, yet the sweep keeps coming back for more: it was retried, not torn down.
        await WaitUntilAsync(() => ReadCount(conn, 88UL) >= 3, cts.Token);

        conn.DeviceInfoFault = null;
        var recovered = ReadCount(conn, 88UL);
        // Let the first successful cycle seed the (still empty) baseline before anything changes.
        await WaitUntilAsync(() => ReadCount(conn, 88UL) >= recovered + 3, cts.Token);
        conn.DeviceReachabilityOverrides[88UL] = DeviceReachability.Removed;

        await WaitUntilAsync(
            () => changes.Any(e => e.EntityId == 88UL && e.Reachability == DeviceReachability.Removed),
            cts.Token);

        await supervisor.StopAllAsync();
        await cts.CancelAsync();
    }

    private static int ReadCount(FakeRustSocketSource.FakeConnection connection, ulong entityId)
    {
        lock (connection.DeviceReadCalls)
        {
            return connection.DeviceReadCalls.Count(c => c.EntityId == entityId);
        }
    }
}
