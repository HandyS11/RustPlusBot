using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Locating;

public sealed class InfoChannelLocatorTests
{
    private static (InfoChannelLocator Locator, ServiceProvider Provider, string ConnectionString, IClock Clock)
        CreateLocator()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var cs = $"DataSource=info-locator-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(cs);
        keepAlive.Open();
        using (var seed = new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options))
        {
            seed.Database.Migrate();
        }

        var services = new ServiceCollection();
        services.AddSingleton(keepAlive);
        services.AddSingleton(clock);
        services.AddScoped(_ => new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options));
        services.AddScoped<IWorkspaceStore, WorkspaceStore>();
        var provider = services.BuildServiceProvider();

        var locator = new InfoChannelLocator(provider.GetRequiredService<IServiceScopeFactory>(), clock);
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
            ChannelKey = WorkspaceChannelKeys.ServerInfo,
            DiscordChannelId = 777UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        return server.Id;
    }

    [Fact]
    public async Task GetChannelIdAsync_returns_provisioned_info_channel()
    {
        var (locator, provider, cs, _) = CreateLocator();
        await using var _p = provider;
        var serverId = await SeedAsync(cs);

        var channelId = await locator.GetChannelIdAsync(10UL, serverId, CancellationToken.None);

        Assert.Equal(777UL, channelId);
    }

    [Fact]
    public async Task GetChannelIdAsync_returns_null_when_not_provisioned()
    {
        var (locator, provider, _, _) = CreateLocator();
        await using var _p = provider;

        Assert.Null(await locator.GetChannelIdAsync(10UL, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Cache_refreshes_after_ttl_expires()
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
            ChannelKey = WorkspaceChannelKeys.ServerInfo,
            DiscordChannelId = 998UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await insertCtx.SaveChangesAsync();

        // Clock still at UnixEpoch — within the 30 s TTL, cache must NOT be reloaded.
        var withinTtlResult = await locator.GetChannelIdAsync(20UL, server.Id, CancellationToken.None);
        Assert.Null(withinTtlResult);

        // Advance the clock past the 30 s TTL — next call must rebuild the cache.
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(31));

        var afterTtlResult = await locator.GetChannelIdAsync(20UL, server.Id, CancellationToken.None);
        Assert.Equal(998UL, afterTtlResult);
    }
}
