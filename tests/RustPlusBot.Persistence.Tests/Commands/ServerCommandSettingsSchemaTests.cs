using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Commands;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Tests.Commands;

public sealed class ServerCommandSettingsSchemaTests
{
    [Fact]
    public async Task ServerCommandSettings_RoundTrips_PrefixAndMuted()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var server = new RustServer
        {
            GuildId = 1UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        var serverId = server.Id;
        context.ServerCommandSettings.Add(new ServerCommandSettings
        {
            ServerId = serverId, GuildId = 1UL, Prefix = "?", Muted = true,
        });
        await context.SaveChangesAsync();

        var read = await context.ServerCommandSettings.SingleAsync(s => s.ServerId == serverId);
        Assert.Equal("?", read.Prefix);
        Assert.True(read.Muted);
    }

    [Fact]
    public async Task RemovingServer_CascadeDeletesItsCommandSettings()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var server = new RustServer
        {
            GuildId = 1UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        context.ServerCommandSettings.Add(new ServerCommandSettings
        {
            ServerId = server.Id, GuildId = 1UL, Prefix = "!", Muted = false,
        });
        await context.SaveChangesAsync();

        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.ServerCommandSettings.ToListAsync());
    }
}
