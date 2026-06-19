using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
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
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class SwitchQueryTests
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

        var cs = $"DataSource=switchquery-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
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
    public async Task SetSmartSwitch_forwards_value_when_connected()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, _) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndSwitchAsync(provider, entityId: 42UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        var ok = await supervisor.SetSmartSwitchAsync(10UL, serverId, 42UL, value: true, cts.Token);

        Assert.True(ok);
        Assert.Contains((42UL, true), source.LastConnection!.SetSwitchCalls);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task SetSmartSwitch_returns_false_when_no_live_socket()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, _) = CreateHarness(source);
        await using var disposeProvider = provider;

        Assert.False(await supervisor.SetSmartSwitchAsync(10UL, Guid.NewGuid(), 42UL, true, CancellationToken.None));
    }

    [Fact]
    public async Task Priming_publishes_state_for_persisted_switch_on_connect()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndSwitchAsync(provider, entityId: 42UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Subscribe BEFORE connecting: SubscribeAsync registers the channel eagerly on THIS thread, so the primed
        // publish (which fires during EnsureConnectionAsync) is observed. Only the enumeration runs in Task.Run.
        var stream = bus.SubscribeAsync<SwitchStateChangedEvent>(cts.Token);
        var received =
            new TaskCompletionSource<SwitchStateChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(
            async () =>
            {
                await foreach (var evt in stream)
                {
                    if (evt.EntityId == 42UL)
                    {
                        received.TrySetResult(evt);
                        break;
                    }
                }
            },
            cts.Token);

        // The fake defaults entity 42 absent → GetSmartSwitchInfoAsync returns null → priming publishes off.
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        var evt = await received.Task.WaitAsync(cts.Token);
        Assert.Equal(42UL, evt.EntityId);
        Assert.False(evt.IsActive); // absent in SwitchStates → null → defaulted off
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task Trigger_publishes_state_change()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var disposeProvider = provider;
        var serverId = await SeedServerWithActiveAndSwitchAsync(provider, entityId: 42UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Subscribe BEFORE raising the trigger so the channel is registered when the publish fires.
        var stream = bus.SubscribeAsync<SwitchStateChangedEvent>(cts.Token);
        var received =
            new TaskCompletionSource<SwitchStateChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(
            async () =>
            {
                await foreach (var e in stream)
                {
                    // Skip the prime (off) and match the trigger (on) for entity 42.
                    if (e is { EntityId: 42UL, IsActive: true })
                    {
                        received.TrySetResult(e);
                        break;
                    }
                }
            },
            cts.Token);

        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        source.LastConnection!.SwitchStates[42UL] = true; // re-read on trigger returns on
        source.LastConnection.RaiseSmartSwitchTriggered(42UL);

        var evt = await received.Task.WaitAsync(cts.Token);
        Assert.True(evt.IsActive);
        await supervisor.StopAllAsync();
    }
}
