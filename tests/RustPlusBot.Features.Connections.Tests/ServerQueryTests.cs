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

public sealed class ServerQueryTests
{
    private static (ServiceProvider Provider, ConnectionSupervisor Supervisor) CreateHarness(
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

        var cs = $"DataSource=serverquery-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
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
        return (provider, provider.GetRequiredService<ConnectionSupervisor>());
    }

    private static async Task<Guid> SeedServerWithActiveAsync(ServiceProvider provider, ulong steamId)
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
            SteamId = steamId,
            ProtectedPlayerToken = "123",
            Status = CredentialStatus.Active,
        });
        await ctx.SaveChangesAsync();
        return server.Id;
    }

    [Fact]
    public async Task GetServerInfo_ReturnsSnapshot_WhenConnected()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        source.LastConnection!.InfoResult = new ServerInfoSnapshot(5, 100, 2, null);
        var snapshot = await supervisor.GetServerInfoAsync(10UL, serverId, cts.Token);

        Assert.NotNull(snapshot);
        Assert.Equal(5, snapshot.Players);
        Assert.Equal(100, snapshot.MaxPlayers);
        Assert.Equal(2, snapshot.QueuedPlayers);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task GetServerInfo_ReturnsNull_WhenNoLiveSocket()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;

        var snapshot = await supervisor.GetServerInfoAsync(10UL, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task GetTime_ReturnsSnapshot_WhenConnected()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        source.LastConnection!.TimeResult = new ServerTimeSnapshot(12.5f, 7f, 19f);
        var snapshot = await supervisor.GetTimeAsync(10UL, serverId, cts.Token);

        Assert.NotNull(snapshot);
        Assert.Equal(12.5f, snapshot.TimeOfDay);
        Assert.Equal(7f, snapshot.Sunrise);
        Assert.Equal(19f, snapshot.Sunset);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task GetTime_ReturnsNull_WhenNoLiveSocket()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;

        var snapshot = await supervisor.GetTimeAsync(10UL, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task GetTeamInfo_ReturnsSnapshot_WhenConnected()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        source.LastConnection!.TeamResult = new TeamInfoSnapshot(
            555UL,
            [
                new TeamMemberSnapshot(555UL, "alice", 1f, 2f, true, true, DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch)
            ]);
        var snapshot = await supervisor.GetTeamInfoAsync(10UL, serverId, cts.Token);

        Assert.NotNull(snapshot);
        Assert.Equal(555UL, snapshot.LeaderSteamId);
        Assert.Single(snapshot.Members);
        Assert.Equal("alice", snapshot.Members[0].Name);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task GetTeamInfo_ReturnsNull_WhenNoLiveSocket()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;

        var snapshot = await supervisor.GetTeamInfoAsync(10UL, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task PromoteToLeader_ReturnsTrueAndForwardsSteamId_WhenConnected()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        source.LastConnection!.PromoteResult = true;
        var promoted = await supervisor.PromoteToLeaderAsync(10UL, serverId, 999UL, cts.Token);

        Assert.True(promoted);
        Assert.Equal(999UL, source.LastConnection.LastPromotedSteamId);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task PromoteToLeader_ReturnsFalse_WhenApiNonSuccess()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        source.LastConnection!.PromoteResult = false;
        var promoted = await supervisor.PromoteToLeaderAsync(10UL, serverId, 999UL, cts.Token);

        Assert.False(promoted);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task PromoteToLeader_ReturnsFalse_WhenNoLiveSocket()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;

        var promoted = await supervisor.PromoteToLeaderAsync(10UL, Guid.NewGuid(), 999UL, CancellationToken.None);

        Assert.False(promoted);
    }

    [Fact]
    public async Task GetMonumentsAsync_returns_empty_when_no_live_socket()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;

        var result = await supervisor.GetMonumentsAsync(10UL, Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(result);
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
