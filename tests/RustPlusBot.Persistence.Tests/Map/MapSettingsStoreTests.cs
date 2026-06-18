using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Persistence.Tests.Map;

public sealed class MapSettingsStoreTests
{
    private static RustServer SeedServer(BotDbContext context)
    {
        var server = new RustServer { GuildId = 1UL, Name = "S", Ip = "1.1.1.1", Port = 28015 };
        context.RustServers.Add(server);
        context.SaveChanges();
        return server;
    }

    [Fact]
    public async Task GetAsync_returns_all_on_when_no_row()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var result = await new MapSettingsStore(context).GetAsync(1UL, Guid.NewGuid());

        Assert.Equal(MapLayerSettings.AllOn, result);
    }

    [Fact]
    public async Task SetLayerAsync_creates_row_and_disables_one_layer_only()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var server = SeedServer(context);

        await new MapSettingsStore(context).SetLayerAsync(1UL, server.Id, MapLayer.Monuments, enabled: false);
        var result = await new MapSettingsStore(context).GetAsync(1UL, server.Id);

        Assert.False(result.Monuments);
        Assert.True(result.Grid);
        Assert.True(result.Markers);
        Assert.True(result.Vendor);
        Assert.True(result.Players);
        Assert.True(result.Rigs);
    }

    [Fact]
    public async Task SetLayerAsync_updates_existing_row()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var server = SeedServer(context);
        var store = new MapSettingsStore(context);

        await store.SetLayerAsync(1UL, server.Id, MapLayer.Players, enabled: false);
        await store.SetLayerAsync(1UL, server.Id, MapLayer.Players, enabled: true);
        var result = await store.GetAsync(1UL, server.Id);

        Assert.True(result.Players);
    }
}
