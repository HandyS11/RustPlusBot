using Microsoft.Data.Sqlite;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Persistence.Tests.Switches;

public sealed class SwitchStoreTests
{
    private static (SwitchStore Store, BotDbContext Context, SqliteConnection Conn) Create()
    {
        var (context, connection) = SqliteContextFixture.Create();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        return (new SwitchStore(context, clock), context, connection);
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
    public async Task Add_then_Get_round_trips_and_defaults_state_off()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        var added = await store.AddAsync(10UL, serverId, 42UL, "Switch 42", pairedByUserId: 7UL);
        var loaded = await store.GetAsync(10UL, serverId, 42UL);

        Assert.NotNull(loaded);
        Assert.Equal(added.Id, loaded.Id);
        Assert.Equal("Switch 42", loaded.Name);
        Assert.Equal(7UL, loaded.PairedByUserId);
        Assert.False(loaded.LastIsActive);
        Assert.Equal(DateTimeOffset.UnixEpoch, loaded.CreatedUtc);
    }

    [Fact]
    public async Task Add_is_idempotent_when_switch_already_exists()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        var first = await store.AddAsync(10UL, serverId, 42UL, "Switch 42", pairedByUserId: 7UL);
        // Simulates the concurrent double-accept race: a second AddAsync for the same identity must not throw,
        // and must return the row the first accept persisted (the unique index rejects the duplicate insert).
        var second = await store.AddAsync(10UL, serverId, 42UL, "Switch 42 (dup)", pairedByUserId: 9UL);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Switch 42", second.Name);
        Assert.Equal(7UL, second.PairedByUserId);
        Assert.Single(await store.ListByServerAsync(10UL, serverId));
    }

    [Fact]
    public async Task Exists_reflects_presence()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        Assert.False(await store.ExistsAsync(10UL, serverId, 42UL));
        await store.AddAsync(10UL, serverId, 42UL, "Switch 42", 7UL);
        Assert.True(await store.ExistsAsync(10UL, serverId, 42UL));
    }

    [Fact]
    public async Task ListByServer_returns_only_that_server()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverA = await SeedServerAsync(context);
        var serverB = await SeedServerAsync(context, ip: "2.2.2.2", name: "T");

        await store.AddAsync(10UL, serverA, 1UL, "A1", 7UL);
        await store.AddAsync(10UL, serverA, 2UL, "A2", 7UL);
        await store.AddAsync(10UL, serverB, 3UL, "B1", 7UL);

        var listA = await store.ListByServerAsync(10UL, serverA);
        Assert.Equal(2, listA.Count);
        Assert.All(listA, s => Assert.Equal(serverA, s.ServerId));
    }

    [Fact]
    public async Task Rename_SetMessageId_UpdateState_mutate_the_row()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddAsync(10UL, serverId, 42UL, "Switch 42", 7UL);

        await store.RenameAsync(10UL, serverId, 42UL, "Front gate");
        await store.SetMessageIdAsync(10UL, serverId, 42UL, 999UL);
        await store.UpdateStateAsync(10UL, serverId, 42UL, isActive: true);

        var loaded = await store.GetAsync(10UL, serverId, 42UL);
        Assert.NotNull(loaded);
        Assert.Equal("Front gate", loaded.Name);
        Assert.Equal(999UL, loaded.MessageId);
        Assert.True(loaded.LastIsActive);
    }

    [Fact]
    public async Task Remove_deletes_the_row()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddAsync(10UL, serverId, 42UL, "Switch 42", 7UL);

        await store.RemoveAsync(10UL, serverId, 42UL);

        Assert.Null(await store.GetAsync(10UL, serverId, 42UL));
    }

    [Fact]
    public async Task Mutators_are_noops_when_switch_absent()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        // Should not throw.
        await store.RenameAsync(10UL, serverId, 42UL, "x");
        await store.SetMessageIdAsync(10UL, serverId, 42UL, 1UL);
        await store.UpdateStateAsync(10UL, serverId, 42UL, true);
        await store.RemoveAsync(10UL, serverId, 42UL);

        Assert.Null(await store.GetAsync(10UL, serverId, 42UL));
    }

    [Fact]
    public async Task SetReachabilityAsync_PersistsTheReachability()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddAsync(1UL, serverId, 42UL, "Door", 7UL);

        await store.SetReachabilityAsync(1UL, serverId, 42UL, DeviceReachability.Removed);

        var sw = await store.GetAsync(1UL, serverId, 42UL);
        Assert.Equal(DeviceReachability.Removed, sw!.Reachability);
    }
}
