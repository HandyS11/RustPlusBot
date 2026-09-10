using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Map;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Tests.Map;

public sealed class ServerMapSettingsSchemaTests
{
    [Fact]
    public async Task ServerMapSettings_persists_with_all_layers_on_by_default()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 1UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        context.ServerMapSettings.Add(new ServerMapSettings
        {
            ServerId = server.Id, GuildId = 1UL
        });
        await context.SaveChangesAsync();

        var read = await context.ServerMapSettings.SingleAsync(s => s.ServerId == server.Id);
        Assert.True(read.ShowGrid);
        Assert.True(read.ShowMarkers);
        Assert.True(read.ShowMonuments);
        Assert.True(read.ShowVendor);
        Assert.True(read.ShowPlayers);
        Assert.True(read.ShowRigs);
        Assert.True(read.ShowTunnels);
    }

    [Fact]
    public async Task RemovingServer_CascadeDeletesItsMapSettings()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 1UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        context.ServerMapSettings.Add(new ServerMapSettings
        {
            ServerId = server.Id, GuildId = 1UL
        });
        await context.SaveChangesAsync();

        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.ServerMapSettings.ToListAsync());
    }
}
