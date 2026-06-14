using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Tests;

public sealed class BotDbContextTests
{
    [Fact]
    public async Task RustServer_RoundTrips_WithSnowflakeGuildId()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        const ulong guildId = 1234567890123456789UL; // larger than long.MaxValue/2; exercises ulong<->long
        context.RustServers.Add(new RustServer
        {
            GuildId = guildId,
            Name = "Main",
            Ip = "127.0.0.1",
            Port = 28082,
            AddedByUserId = 42UL,
        });
        await context.SaveChangesAsync();

        var loaded = await context.RustServers.SingleAsync();
        Assert.Equal(guildId, loaded.GuildId);
        Assert.Equal("Main", loaded.Name);
    }
}
