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

namespace RustPlusBot.Features.Connections.Tests;

public sealed class TeamChatSenderTests
{
    private static (ServiceProvider Provider, ConnectionSupervisor Supervisor, IEventBus Bus) CreateHarness(
        FakeRustSocketSource source)
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Unprotect(Arg.Any<string>()).Returns(c => c.Arg<string>());
        var dm = Substitute.For<IUserDmSender>();

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton(protector);
        services.AddSingleton(dm);
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        var cs = $"DataSource=teamchat-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
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
        return (provider, provider.GetRequiredService<ConnectionSupervisor>(),
            provider.GetRequiredService<IEventBus>());
    }

    private static async Task<Guid> SeedServerWithActiveAsync(ServiceProvider provider, ulong steamId)
    {
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var server = new RustServer { GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015 };
        ctx.RustServers.Add(server);
        ctx.PlayerCredentials.Add(new PlayerCredential
        {
            GuildId = 10UL, RustServerId = server.Id, OwnerUserId = 1UL, SteamId = steamId,
            ProtectedPlayerToken = "123", Status = CredentialStatus.Active,
        });
        await ctx.SaveChangesAsync();
        return server.Id;
    }

    [Fact]
    public async Task Inbound_line_publishes_event_with_active_player_flag()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var _ = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = new TaskCompletionSource<TeamMessageReceivedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = Task.Run(async () =>
        {
            await foreach (var e in bus.SubscribeAsync<TeamMessageReceivedEvent>(cts.Token))
            {
                if (received.TrySetResult(e))
                {
                    break;
                }
            }
        }, cts.Token);

        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => source.LastConnection is not null, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);
        source.LastConnection!.RaiseTeamMessage(new TeamChatLine(999UL, "Bob", "[Alice] hi"));

        var evt = await received.Task.WaitAsync(cts.Token);
        Assert.Equal("Bob", evt.SenderName);
        Assert.Equal("[Alice] hi", evt.Message);
        Assert.False(evt.FromActivePlayer);

        await supervisor.StopAllAsync();
        await subscription;
    }

    [Fact]
    public async Task SendAsync_routes_to_live_socket_when_connected()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, _) = CreateHarness(source);
        await using var _p = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        var result = await supervisor.SendAsync(10UL, serverId, "[Alice] hi", cts.Token);

        Assert.Equal(TeamChatSendResult.Sent, result);
        Assert.Contains("[Alice] hi", source.LastConnection!.SentMessages);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task SendAsync_returns_NotConnected_when_no_socket()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, _) = CreateHarness(source);
        await using var _p = provider;

        var result = await supervisor.SendAsync(10UL, Guid.NewGuid(), "hi", CancellationToken.None);

        Assert.Equal(TeamChatSendResult.NotConnected, result);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(10, ct);
        }
    }
}
