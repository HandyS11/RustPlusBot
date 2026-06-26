using Microsoft.Data.Sqlite;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.StorageMonitors;

namespace RustPlusBot.Persistence.Tests.StorageMonitors;

public sealed class StorageMonitorStoreTests
{
    private static (StorageMonitorStore Store, BotDbContext Context, SqliteConnection Conn) Create()
    {
        var (context, connection) = SqliteContextFixture.Create();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        return (new StorageMonitorStore(context, clock), context, connection);
    }

    private static async Task<Guid> SeedServerAsync(BotDbContext context, string ip = "1.1.1.1", string name = "S")
    {
        var server = new RustServer
        {
            GuildId = 10UL, Name = name, Ip = ip, Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }

    [Fact]
    public async Task Add_then_Get_round_trips()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        var added = await store.AddAsync(10UL, serverId, 777UL, "Box", pairedByUserId: 5UL);
        var loaded = await store.GetAsync(10UL, serverId, 777UL);

        Assert.NotNull(loaded);
        Assert.Equal(added.Id, loaded.Id);
        Assert.Equal("Box", loaded.Name);
        Assert.Equal(5UL, loaded.PairedByUserId);
        Assert.Equal(DateTimeOffset.UnixEpoch, loaded.CreatedUtc);
    }

    [Fact]
    public async Task Add_is_idempotent_when_monitor_already_exists()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        var first = await store.AddAsync(10UL, serverId, 777UL, "Box", pairedByUserId: 5UL);
        // Simulates the concurrent double-accept race: a second AddAsync for the same identity must not throw,
        // and must return the row the first accept persisted (the unique index rejects the duplicate insert).
        var second = await store.AddAsync(10UL, serverId, 777UL, "Box again", pairedByUserId: 6UL);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Box", second.Name);
        Assert.Equal(5UL, second.PairedByUserId);
        Assert.Single(await store.ListByServerAsync(10UL, serverId));
    }

    [Fact]
    public async Task Exists_reflects_presence()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        Assert.False(await store.ExistsAsync(10UL, serverId, 777UL));
        await store.AddAsync(10UL, serverId, 777UL, "Box", 5UL);
        Assert.True(await store.ExistsAsync(10UL, serverId, 777UL));
    }

    [Fact]
    public async Task ListByServer_returns_only_that_server_ordered_by_CreatedUtc()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverA = await SeedServerAsync(context);
        var serverB = await SeedServerAsync(context, ip: "2.2.2.2", name: "T");

        await store.AddAsync(10UL, serverA, 1UL, "A1", 5UL);
        await store.AddAsync(10UL, serverA, 2UL, "A2", 5UL);
        await store.AddAsync(10UL, serverB, 3UL, "B1", 5UL);

        var listA = await store.ListByServerAsync(10UL, serverA);
        Assert.Equal(2, listA.Count);
        Assert.All(listA, s => Assert.Equal(serverA, s.ServerId));
    }

    [Fact]
    public async Task Rename_and_SetMessageId_mutate_the_row()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddAsync(10UL, serverId, 777UL, "Box", 5UL);

        await store.RenameAsync(10UL, serverId, 777UL, "Loot Room");
        await store.SetMessageIdAsync(10UL, serverId, 777UL, 999UL);

        var loaded = await store.GetAsync(10UL, serverId, 777UL);
        Assert.NotNull(loaded);
        Assert.Equal("Loot Room", loaded.Name);
        Assert.Equal(999UL, loaded.MessageId);
    }

    [Fact]
    public async Task Remove_deletes_the_row()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddAsync(10UL, serverId, 777UL, "Box", 5UL);

        await store.RemoveAsync(10UL, serverId, 777UL);

        Assert.Null(await store.GetAsync(10UL, serverId, 777UL));
    }

    [Fact]
    public async Task Mutators_are_noops_when_monitor_absent()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        // Should not throw.
        await store.RenameAsync(10UL, serverId, 777UL, "x");
        await store.SetMessageIdAsync(10UL, serverId, 777UL, 1UL);
        await store.RemoveAsync(10UL, serverId, 777UL);

        Assert.Null(await store.GetAsync(10UL, serverId, 777UL));
    }
}
