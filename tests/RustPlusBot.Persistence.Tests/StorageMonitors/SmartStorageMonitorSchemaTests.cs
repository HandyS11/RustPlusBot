using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.StorageMonitors;

namespace RustPlusBot.Persistence.Tests.StorageMonitors;

public sealed class SmartStorageMonitorSchemaTests
{
    [Fact]
    public async Task SmartStorageMonitor_round_trips_through_sqlite()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        var entity = new SmartStorageMonitor
        {
            GuildId = 10UL,
            ServerId = server.Id,
            EntityId = 777UL,
            Name = "Storage Monitor 777",
            PairedByUserId = 5UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
        context.Set<SmartStorageMonitor>().Add(entity);
        await context.SaveChangesAsync();

        var loaded = await context.Set<SmartStorageMonitor>().SingleAsync();
        Assert.Equal(777UL, loaded.EntityId);
        Assert.Equal("Storage Monitor 777", loaded.Name);
        Assert.Null(loaded.MessageId);
    }

    [Fact]
    public async Task SmartStorageMonitor_cascades_when_server_removed()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        context.Set<SmartStorageMonitor>().Add(new SmartStorageMonitor
        {
            GuildId = 10UL,
            ServerId = server.Id,
            EntityId = 777UL,
            Name = "Storage Monitor 777",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.Set<SmartStorageMonitor>().ToListAsync());
    }

    [Fact]
    public async Task SmartStorageMonitor_unique_index_rejects_duplicate_entity()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        context.Set<SmartStorageMonitor>().Add(new SmartStorageMonitor
        {
            GuildId = 10UL,
            ServerId = server.Id,
            EntityId = 777UL,
            Name = "A",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        context.Set<SmartStorageMonitor>().Add(new SmartStorageMonitor
        {
            GuildId = 10UL,
            ServerId = server.Id,
            EntityId = 777UL,
            Name = "B",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
