using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Persistord.Testing;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Locating;

public sealed class SetupChannelLocatorTests
{
    private static (SetupChannelLocator Locator, ServiceProvider Provider, string ConnectionString, IClock Clock)
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

        var locator = new SetupChannelLocator(provider.GetRequiredService<IServiceScopeFactory>(), clock);
        return (locator, provider, cs, clock);
    }

    private static async Task SeedAsync(string connectionString)
    {
        await using var context =
            new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connectionString).Options);

        // The global #setup channel row (RustServerId null) the locator must resolve.
        context.ProvisionedChannels.Add(new ProvisionedChannel
        {
            GuildId = 10UL,
            RustServerId = null,
            ChannelKey = WorkspaceChannelKeys.Setup,
            DiscordChannelId = 777UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });

        // A server-scoped row under the same key must be skipped (defensive; cannot occur in practice).
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
            ChannelKey = WorkspaceChannelKeys.Setup,
            DiscordChannelId = 888UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetChannelIdAsync_returns_global_setup_channel()
    {
        var (locator, provider, cs, _) = CreateLocator();
        await using var _p = provider;
        using var _l = locator;
        await SeedAsync(cs);

        var id = await locator.GetChannelIdAsync(10UL, CancellationToken.None);

        Assert.Equal(777UL, id);
    }

    [Fact]
    public async Task GetChannelIdAsync_returns_null_when_unprovisioned()
    {
        var (locator, provider, _, _) = CreateLocator();
        await using var _p = provider;
        using var _l = locator;

        var id = await locator.GetChannelIdAsync(10UL, CancellationToken.None);

        Assert.Null(id);
    }

    [Fact]
    public async Task GetChannelIdAsync_serves_cached_entries_until_ttl_expires()
    {
        var (locator, provider, cs, clock) = CreateLocator();
        await using var _p = provider;
        using var _l = locator;

        // First read builds an empty cache.
        Assert.Null(await locator.GetChannelIdAsync(10UL, CancellationToken.None));

        await SeedAsync(cs);

        // Within the TTL the stale empty cache is served.
        Assert.Null(await locator.GetChannelIdAsync(10UL, CancellationToken.None));

        // Past the TTL the cache refreshes and finds the row.
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(31));
        Assert.Equal(777UL, await locator.GetChannelIdAsync(10UL, CancellationToken.None));
    }
}
