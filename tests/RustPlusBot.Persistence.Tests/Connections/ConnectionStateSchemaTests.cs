using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Connections;

namespace RustPlusBot.Persistence.Tests.Connections;

public sealed class ConnectionStateSchemaTests
{
    [Fact]
    public async Task ConnectionState_RoundTrips_StatusAndPlayerCount()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var serverId = Guid.NewGuid();
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
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var serverId = Guid.NewGuid();
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
}
