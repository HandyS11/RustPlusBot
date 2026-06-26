using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Connections.Tests.Fakes;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class StorageMonitorSeamTests
{
    [Fact]
    public void Fake_RaiseStorageMonitorTriggered_DeliversContents()
    {
        var source = new FakeRustSocketSource();
        var connection = (FakeRustSocketSource.FakeConnection)source.Create("1.2.3.4", 28015, 100UL, "tok");

        StorageMonitorTrigger? received = null;
        connection.StorageMonitorTriggered += (_, t) => received = t;

        var contents = new StorageContentsSnapshot(24, true, DateTimeOffset.UnixEpoch,
            [new StorageItemSnapshot(-151838493, 500, false)]);
        connection.RaiseStorageMonitorTriggered(777UL, contents);

        Assert.NotNull(received);
        Assert.Equal(777UL, received.EntityId);
        var item = Assert.Single(received.Contents.Items);
        Assert.Equal(-151838493, item.ItemId);
        Assert.Equal(500, item.Quantity);
    }
}
