using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Switches;

namespace RustPlusBot.Persistence.Tests.Switches;

public sealed class SmartSwitchSchemaTests
{
    [Fact]
    public async Task SmartSwitch_round_trips_through_sqlite()
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

        var entity = new SmartSwitch
        {
            GuildId = 10UL,
            ServerId = server.Id,
            EntityId = 42UL,
            Name = "Switch 42",
            PairedByUserId = 7UL,
            LastIsActive = true,
            CreatedUtc = DateTimeOffset.UnixEpoch,
        };
        context.Set<SmartSwitch>().Add(entity);
        await context.SaveChangesAsync();

        var loaded = await context.Set<SmartSwitch>().SingleAsync();
        Assert.Equal(42UL, loaded.EntityId);
        Assert.Equal("Switch 42", loaded.Name);
        Assert.True(loaded.LastIsActive);
        Assert.Null(loaded.MessageId);
    }

    [Fact]
    public async Task SmartSwitch_cascades_when_server_removed()
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
        context.Set<SmartSwitch>().Add(new SmartSwitch
        {
            GuildId = 10UL,
            ServerId = server.Id,
            EntityId = 42UL,
            Name = "Switch 42",
            CreatedUtc = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.Set<SmartSwitch>().ToListAsync());
    }

    [Fact]
    public async Task SmartSwitch_unique_index_rejects_duplicate_entity()
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
        context.Set<SmartSwitch>().Add(new SmartSwitch
        {
            GuildId = 10UL,
            ServerId = server.Id,
            EntityId = 42UL,
            Name = "A",
            CreatedUtc = DateTimeOffset.UnixEpoch,
        });
        context.Set<SmartSwitch>().Add(new SmartSwitch
        {
            GuildId = 10UL,
            ServerId = server.Id,
            EntityId = 42UL,
            Name = "B",
            CreatedUtc = DateTimeOffset.UnixEpoch,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
