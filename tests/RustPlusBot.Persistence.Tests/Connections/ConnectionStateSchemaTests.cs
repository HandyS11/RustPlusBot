using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Tests.Connections;

public sealed class ConnectionStateSchemaTests
{
    [Fact]
    public async Task ConnectionState_RoundTrips_StatusAndPlayerCount()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        var serverId = server.Id;
        context.ConnectionStates.Add(new ConnectionState
        {
            RustServerId = serverId,
            GuildId = 10UL,
            ActiveCredentialId = Guid.NewGuid(),
            Status = ConnectionStatus.Connected,
            PlayerCount = 42,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        var read = await context.ConnectionStates.SingleAsync(s => s.RustServerId == serverId);
        Assert.Equal(ConnectionStatus.Connected, read.Status);
        Assert.Equal(42, read.PlayerCount);
    }

    [Fact]
    public async Task ConnectionState_RoundTrips_NullPlayerCount()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        var serverId = server.Id;
        context.ConnectionStates.Add(new ConnectionState
        {
            RustServerId = serverId,
            GuildId = 10UL,
            Status = ConnectionStatus.Connecting,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        var read = await context.ConnectionStates.SingleAsync(s => s.RustServerId == serverId);
        Assert.Null(read.PlayerCount);
    }

    [Fact]
    public async Task RemovingServer_CascadeDeletesItsConnectionState()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        context.ConnectionStates.Add(new ConnectionState
        {
            RustServerId = server.Id,
            GuildId = 10UL,
            Status = ConnectionStatus.Connected,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.ConnectionStates.ToListAsync());
    }
}
