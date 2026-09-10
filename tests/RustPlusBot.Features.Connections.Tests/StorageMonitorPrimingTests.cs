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

public sealed class StorageMonitorPrimingTests
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
        await store.AddAsync(10UL, server.Id, entityId, $"Storage Monitor {entityId}", 1UL);
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
    public async Task Priming_publishes_StorageMonitorTriggeredEvent_for_persisted_monitor_on_connect()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndMonitorAsync(provider, entityId: 777UL);

        var contents = new StorageContentsSnapshot(48, null, null, []);

        // Pre-stage the storage contents BEFORE EnsureConnectionAsync so the prime loop sees it.
        source.EnqueueStorageInfo(777UL, contents);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Subscribe BEFORE connecting: SubscribeAsync registers the channel eagerly on THIS thread, so the primed
        // publish (which fires during EnsureConnectionAsync) is observed. Only the enumeration runs in Task.Run.
        var stream = bus.SubscribeAsync<StorageMonitorTriggeredEvent>(cts.Token);
        var received =
            new TaskCompletionSource<StorageMonitorTriggeredEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(
            async () =>
            {
                await foreach (var evt in stream)
                {
                    if (evt.EntityId == 777UL)
                    {
                        received.TrySetResult(evt);
                        break;
                    }
                }
            },
            cts.Token);

        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        var result = await received.Task.WaitAsync(cts.Token);
        Assert.Equal(777UL, result.EntityId);
        Assert.Equal(48, result.Contents.Capacity);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task GetStorageContentsAsync_returns_null_when_no_live_socket()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, _) = CreateHarness(source);
        await using var disposeProvider = provider;

        var result = await supervisor.GetStorageContentsAsync(10UL, Guid.NewGuid(), 777UL, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetStorageContentsAsync_returns_contents_when_live_socket_exists()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, _) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndMonitorAsync(provider, entityId: 888UL);

        var expectedContents = new StorageContentsSnapshot(24, null, null,
            [new StorageItemSnapshot(-151838493, 100, false)]);
        source.EnqueueStorageInfo(888UL, expectedContents);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        var result = await supervisor.GetStorageContentsAsync(10UL, serverId, 888UL, cts.Token);

        Assert.NotNull(result);
        Assert.Equal(24, result.Capacity);
        var item = Assert.Single(result.Items);
        Assert.Equal(-151838493, item.ItemId);
        Assert.Equal(100, item.Quantity);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task Trigger_publishes_StorageMonitorTriggeredEvent_on_storage_change()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndMonitorAsync(provider, entityId: 999UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Subscribe BEFORE raising the trigger so the channel is registered when the publish fires.
        var stream = bus.SubscribeAsync<StorageMonitorTriggeredEvent>(cts.Token);
        var received =
            new TaskCompletionSource<StorageMonitorTriggeredEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(
            async () =>
            {
                await foreach (var e in stream)
                {
                    // Skip any prime (null → not published) and match the trigger for entity 999.
                    if (e is { EntityId: 999UL })
                    {
                        received.TrySetResult(e);
                        break;
                    }
                }
            },
            cts.Token);

        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        var triggerContents = new StorageContentsSnapshot(48, null, null,
            [new StorageItemSnapshot(-151838493, 500, false)]);
        source.LastConnection!.RaiseStorageMonitorTriggered(999UL, triggerContents);

        var evt = await received.Task.WaitAsync(cts.Token);
        Assert.Equal(999UL, evt.EntityId);
        Assert.Equal(48, evt.Contents.Capacity);
        await supervisor.StopAllAsync();
    }
}
