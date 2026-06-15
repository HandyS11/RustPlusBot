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

public sealed class TeamChatChannelLocatorTests
{
    private static (TeamChatChannelLocator Locator, ServiceProvider Provider, string ConnectionString)
        CreateLocator()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var cs = $"DataSource=locator-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
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

        var locator = new TeamChatChannelLocator(provider.GetRequiredService<IServiceScopeFactory>(), clock);
        return (locator, provider, cs);
    }

    private static async Task<Guid> SeedAsync(string connectionString)
    {
        await using var context =
            new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connectionString).Options);

        var server = new RustServer { GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015 };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        context.ProvisionedChannels.Add(new ProvisionedChannel
        {
            GuildId = 10UL,
            RustServerId = server.Id,
            ChannelKey = "teamchat",
            DiscordChannelId = 777UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        return server.Id;
    }

    [Fact]
    public async Task GetChannelIdAsync_returns_provisioned_channel()
    {
        var (locator, provider, cs) = CreateLocator();
        await using var _ = provider;
        var serverId = await SeedAsync(cs);

        var channelId = await locator.GetChannelIdAsync(10UL, serverId, CancellationToken.None);

        Assert.Equal(777UL, channelId);
    }

    [Fact]
    public async Task ResolveAsync_maps_channel_to_guild_and_server()
    {
        var (locator, provider, cs) = CreateLocator();
        await using var _ = provider;
        var serverId = await SeedAsync(cs);

        var resolved = await locator.ResolveAsync(777UL, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(10UL, resolved!.Value.GuildId);
        Assert.Equal(serverId, resolved.Value.ServerId);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_for_unknown_channel()
    {
        var (locator, provider, cs) = CreateLocator();
        await using var _p = provider;
        await SeedAsync(cs);

        Assert.Null(await locator.ResolveAsync(123456UL, CancellationToken.None));
    }
}
