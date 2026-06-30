using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
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

public sealed class SwitchPrimingTests
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

        var cs = $"DataSource=switchpriming-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(cs);
        keepAlive.Open();
        using (var seed = new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options))
        {
            seed.Database.Migrate();
        }

        services.AddSingleton(keepAlive);
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
        }));
        services.AddSingleton<ConnectionSecurity>();
        services.AddSingleton<ConnectionSupervisor>();

        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<ConnectionSupervisor>(), bus);
    }

    private static async Task<Guid> SeedServerWithActiveAndSwitchAsync(ServiceProvider provider, ulong entityId)
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
        var store = scope.ServiceProvider.GetRequiredService<ISwitchStore>();
        await store.AddAsync(10UL, server.Id, entityId, $"Switch {entityId}", 1UL);
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
    public async Task Prime_RemovedSwitch_PublishesRemovedReachability_AndNoActiveState()
    {
        // Arrange: a managed switch exists; the fake connection returns a Removed reading for it.
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndSwitchAsync(provider, entityId: 42UL);

        // Configure the fake's reachability override so GetSmartDeviceInfoAsync returns Removed for entity 42.
        // This must be staged before EnsureConnectionAsync so it's in place when the prime loop runs.
        // FakeConnection is created inside Create(), so we stage via the source's pre-connect hook, but
        // DeviceReachabilityOverrides is on FakeConnection — we set it after the first Create() via
        // LastConnection. However, Create() fires during EnsureConnectionAsync (inside the background task),
        // so we can't access LastConnection before EnsureConnectionAsync completes. Instead we use
        // a connect-outcome hook: stage Connected so the connection is created; then wait for HasLiveSocket.
        //
        // To avoid the race, we subscribe to DeviceReachabilityChangedEvent before connecting and
        // set the override on the source's pending-storage approach is not available for switches.
        // The brief says to use DeviceReachabilityOverrides — set it before EnsureConnectionAsync
        // by using a custom FakeRustSocketSource subclass or by seeding it via a staging dictionary.
        //
        // The cleanest race-free approach: subscribe first, then EnsureConnectionAsync, then wait
        // for HasLiveSocket, then check published events — but priming fires BEFORE HasLiveSocket
        // check is possible via polling. We use the same approach as AlarmPrimingTests: subscribe
        // to the reachability event (which fires only after the new prime code), wait for it, then
        // assert absence of SmartDeviceTriggeredEvent{IsActive:false}.
        //
        // To stage the override before prime: add a pre-stage dictionary on FakeRustSocketSource
        // analogous to _pendingStorageContents (which transfers to FakeConnection at Create time).
        // However the brief says the fake already has DeviceReachabilityOverrides and to "use it".
        // Per the brief Task 4 note: "Read that fake to learn its exact API."
        // FakeRustSocketSource already has _pendingStorageContents transferred at Create time;
        // FakeConnection.DeviceReachabilityOverrides exists but has no pre-stage path in the source.
        // We add a minimal PendingDeviceReachabilityOverrides staging dict to FakeRustSocketSource
        // (transferred at Create time, like _pendingStorageContents) — documented in the report.
        source.StageDeviceReachability(42UL, DeviceReachability.Removed);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Capture all published events on two channels.
        var reachabilityEvents = new System.Collections.Concurrent.ConcurrentQueue<DeviceReachabilityChangedEvent>();
        var triggeredEvents = new System.Collections.Concurrent.ConcurrentQueue<SmartDeviceTriggeredEvent>();

        var reachSub = bus.SubscribeAsync<DeviceReachabilityChangedEvent>(cts.Token);
        var trigSub = bus.SubscribeAsync<SmartDeviceTriggeredEvent>(cts.Token);

        _ = Task.Run(async () =>
        {
            await foreach (var e in reachSub)
            {
                reachabilityEvents.Enqueue(e);
            }
        }, CancellationToken.None);

        _ = Task.Run(async () =>
        {
            await foreach (var e in trigSub)
            {
                triggeredEvents.Enqueue(e);
            }
        }, CancellationToken.None);

        // Act: drive the connect/prime path.
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        // Wait for the reachability event for entity 42 to be published (definite signal that prime completed).
        await WaitUntilAsync(() => reachabilityEvents.Any(e => e.EntityId == 42UL), cts.Token);

        // Give a moment for any spurious SmartDeviceTriggeredEvent to appear if incorrectly published.
        await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);

        // Assert: DeviceReachabilityChangedEvent{EntityId=42, Reachability=Removed} was published.
        Assert.Contains(reachabilityEvents, e =>
            e is { EntityId: 42UL, Reachability: DeviceReachability.Removed });

        // Assert: SmartDeviceTriggeredEvent{EntityId=42, IsActive=false} was NOT published.
        Assert.DoesNotContain(triggeredEvents, e =>
            e is { EntityId: 42UL, IsActive: false });

        await supervisor.StopAllAsync();
    }
}
