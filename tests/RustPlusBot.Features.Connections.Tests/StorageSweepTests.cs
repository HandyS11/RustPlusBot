using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Persistord.Testing;
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

public sealed class StorageSweepTests
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

    private static async Task<Guid> SeedServerWithActiveAndMonitorAsync(ServiceProvider provider, ulong entityId)
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
        var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
        await store.AddAsync(10UL, server.Id, entityId, $"Monitor {entityId}", 1UL);
        return server.Id;
    }

    [Fact]
    public async Task Sweep_republishes_contents_for_reachable_storage_monitor()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndMonitorAsync(provider, entityId: 88UL);
        source.EnqueueStorageInfo(88UL,
            new StorageContentsSnapshot(48, null, null, [new StorageItemSnapshot(100, 5, false)]));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Subscribe BEFORE connecting so the prime publish is observed. The prime publishes the first
        // contents event; the periodic reachability sweep must republish contents so embeds keep tracking
        // in-game changes even when no broadcast arrives (broadcasts alone are unreliable for storage).
        var stream = bus.SubscribeAsync<StorageMonitorTriggeredEvent>(cts.Token);
        var second =
            new TaskCompletionSource<StorageMonitorTriggeredEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(
            async () =>
            {
                var seen = 0;
                await foreach (var evt in stream)
                {
                    if (evt.EntityId != 88UL)
                    {
                        continue;
                    }

                    seen++;
                    if (seen >= 2)
                    {
                        second.TrySetResult(evt);
                        break;
                    }
                }
            },
            cts.Token);

        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        var evt = await second.Task.WaitAsync(cts.Token);
        Assert.Equal(88UL, evt.EntityId);
        Assert.NotNull(evt.Contents);
        Assert.Single(evt.Contents.Items);
        await supervisor.StopAllAsync();
    }
}
