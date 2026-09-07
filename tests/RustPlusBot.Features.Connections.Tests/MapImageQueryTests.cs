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
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class MapImageQueryTests
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

        var cs = $"DataSource=mapimage-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
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
        services.AddSingleton<ConnectionSecurity>();
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
    public async Task GetMapImage_ReturnsBytes_WhenConnected()
    {
        // Staged before connect: the connected window resolves the map once, on the marker poll's first
        // pass, so anything set afterwards would lose the race against that fetch.
        var source = new FakeRustSocketSource
        {
            LastConnectionSetup = c => c.MapImageResult = [1, 2, 3],
        };
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        var image = await supervisor.GetMapImageAsync(10UL, serverId, cts.Token);

        Assert.Equal(new byte[]
        {
            1, 2, 3
        }, image);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task GetMapDimensions_ServesRepeatReadsFromTheConnectedWindow_WithoutRefetching()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        // The marker poll resolves dimensions once for the connected window.
        var connection = source.LastConnection!;
        await WaitUntilAsync(() => connection.MapFetchCount > 0, cts.Token);
        var afterConnect = connection.MapFetchCount;

        for (var i = 0; i < 5; i++)
        {
            Assert.NotNull(await supervisor.GetMapDimensionsAsync(10UL, serverId, cts.Token));
        }

        // On the real socket each fetch downloads the whole map JPEG just to read width/height, and the
        // team panel re-renders several times a minute. Repeat reads must not touch the socket.
        Assert.Equal(afterConnect, connection.MapFetchCount);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task GetMonuments_ServesRepeatReadsFromTheConnectedWindow_WithoutRefetching()
    {
        var source = new FakeRustSocketSource();
        source.SetMonuments([new MonumentSnapshot("large_oil_rig", 100f, 200f)]);
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        // The marker poll resolves monuments once for the connected window, on its way to the oil rigs.
        var connection = source.LastConnection!;
        await WaitUntilAsync(() => connection.MapFetchCount > 0, cts.Token);
        var afterConnect = connection.MapFetchCount;

        for (var i = 0; i < 5; i++)
        {
            Assert.NotEmpty(await supervisor.GetMonumentsAsync(10UL, serverId, cts.Token));
        }

        // Monuments come from GetMap, which ships the whole map JPEG, and #map recomposes every 30s.
        // Repeat reads must not touch the socket: monuments are fixed for a wipe.
        Assert.Equal(afterConnect, connection.MapFetchCount);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task ConnectedWindow_IssuesASingleMapFetch_ForDimensionsMonumentsAndImage()
    {
        var source = new FakeRustSocketSource
        {
            LastConnectionSetup = c => c.MapImageResult = [1, 2, 3],
        };
        source.SetMonuments([new MonumentSnapshot("large_oil_rig", 100f, 200f)]);
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        var connection = source.LastConnection!;
        await WaitUntilAsync(() => connection.MapFetchCount > 0, cts.Token);

        Assert.NotNull(await supervisor.GetMapDimensionsAsync(10UL, serverId, cts.Token));
        Assert.NotEmpty(await supervisor.GetMonumentsAsync(10UL, serverId, cts.Token));
        Assert.NotNull(await supervisor.GetMapImageAsync(10UL, serverId, cts.Token));

        // Rust+ answers dimensions, monuments and the JPEG from one GetMap response, so a connected window
        // has no reason to pull the map more than once: it is fixed for the wipe, and a reconnect (which is
        // exactly when a new map can appear) tears the window down.
        Assert.Equal(1, connection.MapFetchCount);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task GetMapImage_ReturnsNull_WhenNoLiveSocket()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;

        var image = await supervisor.GetMapImageAsync(10UL, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(image);
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
