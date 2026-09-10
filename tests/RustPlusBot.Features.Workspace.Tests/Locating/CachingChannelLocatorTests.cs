using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Persistord.Testing;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Locating;

/// <summary>
/// Minimal concrete subclass used only in this test assembly to exercise the shared base behaviour.
/// Uses the ServerEvents key as a representative channel key.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="clock">Drives the cache TTL.</param>
internal sealed class TestChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : CachingChannelLocator(scopeFactory, clock, WorkspaceChannelKeys.ServerEvents);

public sealed class CachingChannelLocatorTests
{
    private static (TestChannelLocator Locator, ServiceProvider Provider, string ConnectionString, IClock Clock)
        CreateLocator()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        // One shared-cache in-memory database several connections can open independently, so a
        // background loop and the test never run concurrent commands on one connection. Creating
        // the first context is what applies the migrations.
        var database = SqliteTestDatabase.Shared();
        var cs = database.ConnectionString;
        database.CreateContext<BotDbContext>(options => new BotDbContext(options)).Dispose();

        var services = new ServiceCollection();
        services.AddSingleton(database);
        services.AddSingleton(clock);
        services.AddScoped(_ => new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options));
        services.AddScoped<IWorkspaceStore, WorkspaceStore>();
        var provider = services.BuildServiceProvider();

        var locator = new TestChannelLocator(provider.GetRequiredService<IServiceScopeFactory>(), clock);
        return (locator, provider, cs, clock);
    }

    private static async Task<Guid> SeedAsync(string connectionString)
    {
        await using var context =
            new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connectionString).Options);

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        context.ProvisionedChannels.Add(new ProvisionedChannel
        {
            GuildId = 10UL,
            RustServerId = server.Id,
            ChannelKey = WorkspaceChannelKeys.ServerEvents,
            DiscordChannelId = 555UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        return server.Id;
    }

    [Fact]
    public async Task GetChannelIdAsync_returns_seeded_channel()
    {
        var (locator, provider, cs, _) = CreateLocator();
        await using var _p = provider;
        var serverId = await SeedAsync(cs);

        var channelId = await locator.GetChannelIdAsync(10UL, serverId, CancellationToken.None);

        Assert.Equal(555UL, channelId);
    }

    [Fact]
    public async Task GetChannelIdAsync_returns_null_for_unknown_server()
    {
        var (locator, provider, _, _) = CreateLocator();
        await using var _p = provider;

        Assert.Null(await locator.GetChannelIdAsync(10UL, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Cache_hit_skips_db_reload_within_ttl()
    {
        var (locator, provider, cs, clock) = CreateLocator();
        await using var _p = provider;

        // Cold load with empty DB — cache built at UnixEpoch, no rows.
        var firstResult = await locator.GetChannelIdAsync(20UL, Guid.NewGuid(), CancellationToken.None);
        Assert.Null(firstResult);

        // Insert a server + channel into the DB after the first load.
        await using var insertCtx =
            new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options);
        var server = new RustServer
        {
            GuildId = 20UL, Name = "T", Ip = "2.2.2.2", Port = 28015
        };
        insertCtx.RustServers.Add(server);
        await insertCtx.SaveChangesAsync();
        insertCtx.ProvisionedChannels.Add(new ProvisionedChannel
        {
            GuildId = 20UL,
            RustServerId = server.Id,
            ChannelKey = WorkspaceChannelKeys.ServerEvents,
            DiscordChannelId = 999UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await insertCtx.SaveChangesAsync();

        // Clock still at UnixEpoch — within the 30 s TTL, cache must NOT be reloaded.
        var withinTtlResult = await locator.GetChannelIdAsync(20UL, server.Id, CancellationToken.None);
        Assert.Null(withinTtlResult);

        // Advance the clock past the 30 s TTL — next call must rebuild the cache.
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(31));

        var afterTtlResult = await locator.GetChannelIdAsync(20UL, server.Id, CancellationToken.None);
        Assert.Equal(999UL, afterTtlResult);
    }

    [Fact]
    public async Task Rows_with_null_server_id_are_skipped()
    {
        var (locator, provider, cs, _) = CreateLocator();
        await using var _p = provider;

        await using var context =
            new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options);

        // Insert a channel row with no RustServerId (global scope) — must be ignored by the locator.
        context.ProvisionedChannels.Add(new ProvisionedChannel
        {
            GuildId = 10UL,
            RustServerId = null,
            ChannelKey = WorkspaceChannelKeys.ServerEvents,
            DiscordChannelId = 444UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        // The locator should return null because the only row has a null server id.
        Assert.Null(await locator.GetChannelIdAsync(10UL, Guid.NewGuid(), CancellationToken.None));
    }
}
