using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Devices;
using RustPlusBot.Domain.Devices;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.StorageMonitors;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Persistence.Tests.Devices;

/// <summary>
///     Pins the seam the pairing coordinators now depend on: both device stores serve the whole shared
///     <see cref="IPairedDeviceStore{TEntity}"/> surface, so a coordinator never names a concrete store.
/// </summary>
public sealed class PairedDeviceStoreTests
{
    [Fact]
    public Task Switch_store_serves_the_shared_device_surface() =>
        AssertSharedSurfaceAsync(context => new SwitchStore(context));

    [Fact]
    public Task StorageMonitor_store_serves_the_shared_device_surface() =>
        AssertSharedSurfaceAsync(context => new StorageMonitorStore(context));

    private static async Task AssertSharedSurfaceAsync<TEntity>(
        Func<BotDbContext, IPairedDeviceStore<TEntity>> create)
        where TEntity : PairedDeviceEntity
    {
        var (context, database) = SqliteContextFixture.Create(new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        await using var _ = database;
        await using var __ = context;
        var store = create(context);
        var serverId = await SeedServerAsync(context);

        Assert.False(await store.ExistsAsync(10UL, serverId, 42UL));
        var added = await store.AddAsync(10UL, serverId, 42UL, "Device 42", pairedByUserId: 7UL);
        Assert.Equal(DateTimeOffset.UnixEpoch, added.CreatedAt);
        Assert.True(await store.ExistsAsync(10UL, serverId, 42UL));

        await store.SetMessageIdAsync(10UL, serverId, 42UL, 999UL);
        await store.RenameAsync(10UL, serverId, 42UL, "Renamed");
        await store.SetReachabilityAsync(10UL, serverId, 42UL, DeviceReachability.NoResponse);

        var loaded = await store.GetAsync(10UL, serverId, 42UL);
        Assert.NotNull(loaded);
        Assert.Equal(999UL, loaded.MessageId);
        Assert.Equal("Renamed", loaded.Name);
        Assert.Equal(DeviceReachability.NoResponse, loaded.Reachability);
        Assert.Single(await store.ListByServerAsync(10UL, serverId));

        await store.RemoveAsync(10UL, serverId, 42UL);
        Assert.False(await store.ExistsAsync(10UL, serverId, 42UL));
    }

    private static async Task<Guid> SeedServerAsync(BotDbContext context)
    {
        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }
}
