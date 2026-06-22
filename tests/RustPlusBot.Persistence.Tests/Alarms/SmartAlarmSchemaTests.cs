using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Tests.Alarms;

public sealed class SmartAlarmSchemaTests
{
    [Fact]
    public async Task RemovingServer_CascadeDeletesAlarms()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var server = new RustServer { GuildId = 10UL, Name = "S", Ip = "1.2.3.4", Port = 28015 };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        context.SmartAlarms.Add(new SmartAlarm
        {
            GuildId = 10UL, ServerId = server.Id, EntityId = 42UL, Name = "Alarm 42", CreatedUtc = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.SmartAlarms.ToListAsync());
    }

    [Fact]
    public async Task DuplicateEntityForSameServer_IsRejected()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var server = new RustServer { GuildId = 10UL, Name = "S", Ip = "1.2.3.4", Port = 28015 };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        context.SmartAlarms.Add(new SmartAlarm { GuildId = 10UL, ServerId = server.Id, EntityId = 7UL, Name = "a", CreatedUtc = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();
        context.SmartAlarms.Add(new SmartAlarm { GuildId = 10UL, ServerId = server.Id, EntityId = 7UL, Name = "b", CreatedUtc = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
