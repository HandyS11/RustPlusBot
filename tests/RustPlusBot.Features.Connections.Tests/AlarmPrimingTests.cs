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

public sealed class AlarmPrimingTests
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

    [Fact]
    public async Task Priming_publishes_observed_state_for_persisted_alarm_on_connect()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndAlarmAsync(provider, entityId: 77UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Subscribe BEFORE connecting: SubscribeAsync registers the channel eagerly on THIS thread, so the primed
        // publish (which fires during EnsureConnectionAsync) is observed. Only the enumeration runs in Task.Run.
        // An alarm prime is an OBSERVATION, not a trigger — a triggered event here would re-ping @everyone on
        // every reconnect while the alarm is active.
        var stream = bus.SubscribeAsync<SmartDeviceStateObservedEvent>(cts.Token);
        var received =
            new TaskCompletionSource<SmartDeviceStateObservedEvent>(TaskCreationOptions
                .RunContinuationsAsynchronously);
        _ = Task.Run(
            async () =>
            {
                await foreach (var evt in stream)
                {
                    if (evt.EntityId == 77UL)
                    {
                        received.TrySetResult(evt);
                        break;
                    }
                }
            },
            cts.Token);

        // The fake defaults entity 77 absent → GetSmartDeviceInfoAsync returns null → priming publishes off.
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        var evt = await received.Task.WaitAsync(cts.Token);
        Assert.Equal(77UL, evt.EntityId);
        Assert.False(evt.IsActive); // absent in SwitchStates → null → defaulted off
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task Priming_reads_alarm_with_alarm_kind()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndAlarmAsync(provider, entityId: 77UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Wait for the prime publish so the device read has definitely happened before asserting.
        var stream = bus.SubscribeAsync<SmartDeviceStateObservedEvent>(cts.Token);
        var received =
            new TaskCompletionSource<SmartDeviceStateObservedEvent>(TaskCreationOptions
                .RunContinuationsAsynchronously);
        _ = Task.Run(
            async () =>
            {
                await foreach (var evt in stream)
                {
                    if (evt.EntityId == 77UL)
                    {
                        received.TrySetResult(evt);
                        break;
                    }
                }
            },
            cts.Token);

        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await received.Task.WaitAsync(cts.Token);
        await supervisor.StopAllAsync();

        // The Rust+ API type-checks entity reads: a switch read against an alarm entity fails and the
        // device would be persisted NoResponse forever. The prime must read the alarm AS an alarm.
        var connection = Assert.IsType<FakeRustSocketSource.FakeConnection>(source.LastConnection);
        lock (connection.DeviceReadCalls)
        {
            Assert.Contains((77UL, SmartDeviceKind.Alarm), connection.DeviceReadCalls);
        }
    }
}
