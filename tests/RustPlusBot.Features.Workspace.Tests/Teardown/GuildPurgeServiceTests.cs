using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Entities;
using RustPlusBot.Domain.Events;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Tests.Teardown;

public sealed class GuildPurgeServiceTests
{
    private static BotDbContext NewContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connection).Options;
        var context = new BotDbContext(options);
        context.Database.Migrate();
        return context;
    }

    [Fact]
    public async Task PurgeGuild_RemovesTargetGuildRows_AndLeavesOtherGuildIntact()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var _ = connection;
        await using var context = NewContext(connection);

        var serverA = new RustServer
        {
            GuildId = 1, Name = "A", Ip = "a", Port = 1
        };
        var serverB = new RustServer
        {
            GuildId = 2, Name = "B", Ip = "b", Port = 2
        };
        context.RustServers.AddRange(serverA, serverB);
        context.SmartSwitches.Add(new SmartSwitch
        {
            GuildId = 1, ServerId = serverA.Id, EntityId = 10, Name = "sw"
        });
        context.ConnectionStates.Add(new ConnectionState
        {
            RustServerId = serverA.Id, GuildId = 1, Status = ConnectionStatus.Connected
        });
        context.EventSubscriptions.Add(new EventSubscription
        {
            GuildId = 1, RustServerId = serverA.Id, EventKey = "cargo"
        });
        context.EventSubscriptions.Add(new EventSubscription
        {
            GuildId = 2, RustServerId = serverB.Id, EventKey = "cargo"
        });
        context.PairedEntities.Add(new PairedEntity
        {
            GuildId = 1, RustServerId = serverA.Id, EntityId = 5, Name = "dev"
        });
        context.GuildSettings.Add(new GuildSettings
        {
            GuildId = 1, Culture = "en"
        });
        context.GuildSettings.Add(new GuildSettings
        {
            GuildId = 2, Culture = "fr"
        });
        await context.SaveChangesAsync();

        var teardown = Substitute.For<IWorkspaceTeardownService>();
        var service = new GuildPurgeService(context, new ServerService(context), teardown);

        await service.PurgeGuildAsync(1);

        await teardown.Received(1).ResetGuildAsync(1, Arg.Any<CancellationToken>());
        Assert.Empty(await context.RustServers.Where(s => s.GuildId == 1).ToListAsync());
        Assert.Empty(await context.SmartSwitches.ToListAsync());
        Assert.Empty(await context.ConnectionStates.ToListAsync());
        Assert.Empty(await context.EventSubscriptions.Where(e => e.GuildId == 1).ToListAsync());
        Assert.Empty(await context.PairedEntities.Where(p => p.GuildId == 1).ToListAsync());
        Assert.Empty(await context.GuildSettings.Where(g => g.GuildId == 1).ToListAsync());

        // Guild 2 untouched.
        Assert.Single(await context.RustServers.Where(s => s.GuildId == 2).ToListAsync());
        Assert.Single(await context.EventSubscriptions.Where(e => e.GuildId == 2).ToListAsync());
        Assert.Single(await context.GuildSettings.Where(g => g.GuildId == 2).ToListAsync());
    }
}
