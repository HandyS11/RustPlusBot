using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Maintenance;

namespace RustPlusBot.Persistence.Tests.Maintenance;

public sealed class DatabaseMaintenanceServiceTests
{
    [Fact]
    public async Task ClearAllAsync_EmptiesEveryTable_AndKeepsSchema()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        context.RustServers.Add(new RustServer
        {
            GuildId = 1, Name = "A", Ip = "a", Port = 1
        });
        context.RustServers.Add(new RustServer
        {
            GuildId = 2, Name = "B", Ip = "b", Port = 2
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

        var service = new DatabaseMaintenanceService(context);
        await service.ClearAllAsync();

        Assert.Empty(await context.RustServers.ToListAsync());
        Assert.Empty(await context.GuildSettings.ToListAsync());

        // Schema still exists: a fresh insert succeeds.
        context.GuildSettings.Add(new GuildSettings
        {
            GuildId = 3, Culture = "en"
        });
        await context.SaveChangesAsync();
        Assert.Single(await context.GuildSettings.ToListAsync());
    }
}
