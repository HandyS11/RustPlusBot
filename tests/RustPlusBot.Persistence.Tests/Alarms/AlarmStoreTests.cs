using System.Globalization;
using Microsoft.Data.Sqlite;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Alarms;

namespace RustPlusBot.Persistence.Tests.Alarms;

public sealed class AlarmStoreTests
{
    private static (AlarmStore Store, BotDbContext Context, SqliteConnection Conn) Create(
        DateTimeOffset? now = null)
    {
        var (context, connection) = SqliteContextFixture.Create();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now ?? DateTimeOffset.UnixEpoch);
        return (new AlarmStore(context, clock), context, connection);
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
    public async Task Add_then_Get_round_trips_fields()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        var added = await store.AddAsync(10UL, serverId, 42UL, "Alarm 42", pairedByUserId: 7UL);
        var loaded = await store.GetAsync(10UL, serverId, 42UL);

        Assert.NotNull(loaded);
        Assert.Equal(added.Id, loaded.Id);
        Assert.Equal("Alarm 42", loaded.Name);
        Assert.Equal(7UL, loaded.PairedByUserId);
        Assert.Equal(DateTimeOffset.UnixEpoch, loaded.CreatedUtc);
        Assert.False(loaded.PingEveryone);
        Assert.False(loaded.RelayToTeamChat);
        Assert.False(loaded.LastIsActive);
        Assert.Null(loaded.LastTriggeredUtc);
    }

    [Fact]
    public async Task Add_is_idempotent_when_alarm_already_exists()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        var first = await store.AddAsync(10UL, serverId, 42UL, "Alarm 42", pairedByUserId: 7UL);
        // Simulates the concurrent double-accept race: second AddAsync for the same identity must not throw,
        // and must return the row the first accept persisted.
        var second = await store.AddAsync(10UL, serverId, 42UL, "Alarm 42 (dup)", pairedByUserId: 9UL);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Alarm 42", second.Name);
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
        await store.AddAsync(10UL, serverId, 42UL, "Alarm 42", 7UL);
        Assert.True(await store.ExistsAsync(10UL, serverId, 42UL));
    }

    [Fact]
    public async Task ListByServer_returns_oldest_first()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var t1 = t0.AddMinutes(1);
        var t2 = t0.AddMinutes(2);

        var (context0, conn0) = SqliteContextFixture.Create();
        await using var _conn = conn0;
        await using var _ctx = context0;
        var serverId = await SeedServerAsync(context0);

        var clock0 = Substitute.For<IClock>();
        clock0.UtcNow.Returns(t2);
        var store0 = new AlarmStore(context0, clock0);
        await store0.AddAsync(10UL, serverId, 3UL, "C", 7UL);

        clock0.UtcNow.Returns(t0);
        var store1 = new AlarmStore(context0, clock0);
        await store1.AddAsync(10UL, serverId, 1UL, "A", 7UL);

        clock0.UtcNow.Returns(t1);
        var store2 = new AlarmStore(context0, clock0);
        await store2.AddAsync(10UL, serverId, 2UL, "B", 7UL);

        var list = await store0.ListByServerAsync(10UL, serverId);
        Assert.Equal(3, list.Count);
        Assert.Equal(t0, list[0].CreatedUtc);
        Assert.Equal(t1, list[1].CreatedUtc);
        Assert.Equal(t2, list[2].CreatedUtc);
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
        Assert.All(listA, a => Assert.Equal(serverA, a.ServerId));
    }

    [Fact]
    public async Task Rename_mutates_the_row()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddAsync(10UL, serverId, 42UL, "Alarm 42", 7UL);

        await store.RenameAsync(10UL, serverId, 42UL, "Front base sensor");

        var loaded = await store.GetAsync(10UL, serverId, 42UL);
        Assert.NotNull(loaded);
        Assert.Equal("Front base sensor", loaded.Name);
    }

    [Fact]
    public async Task SetMessageId_mutates_the_row()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddAsync(10UL, serverId, 42UL, "Alarm 42", 7UL);

        await store.SetMessageIdAsync(10UL, serverId, 42UL, 999UL);

        var loaded = await store.GetAsync(10UL, serverId, 42UL);
        Assert.NotNull(loaded);
        Assert.Equal(999UL, loaded.MessageId);
    }

    [Fact]
    public async Task SetPingEveryone_toggles_the_flag()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddAsync(10UL, serverId, 42UL, "Alarm 42", 7UL);

        await store.SetPingEveryoneAsync(10UL, serverId, 42UL, true);
        var loaded = await store.GetAsync(10UL, serverId, 42UL);
        Assert.NotNull(loaded);
        Assert.True(loaded.PingEveryone);

        await store.SetPingEveryoneAsync(10UL, serverId, 42UL, false);
        loaded = await store.GetAsync(10UL, serverId, 42UL);
        Assert.NotNull(loaded);
        Assert.False(loaded.PingEveryone);
    }

    [Fact]
    public async Task SetRelayToTeamChat_toggles_the_flag()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddAsync(10UL, serverId, 42UL, "Alarm 42", 7UL);

        await store.SetRelayToTeamChatAsync(10UL, serverId, 42UL, true);
        var loaded = await store.GetAsync(10UL, serverId, 42UL);
        Assert.NotNull(loaded);
        Assert.True(loaded.RelayToTeamChat);

        await store.SetRelayToTeamChatAsync(10UL, serverId, 42UL, false);
        loaded = await store.GetAsync(10UL, serverId, 42UL);
        Assert.NotNull(loaded);
        Assert.False(loaded.RelayToTeamChat);
    }

    [Fact]
    public async Task UpdateStateAsync_active_sets_state_and_triggered_time()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddAsync(10UL, serverId, 42UL, "Alarm 42", 1UL);

        var t = DateTimeOffset.Parse("2026-06-22T12:00:00Z", CultureInfo.InvariantCulture);
        await store.UpdateStateAsync(10UL, serverId, 42UL, isActive: true, triggeredUtc: t);

        var a = await store.GetAsync(10UL, serverId, 42UL);
        Assert.True(a!.LastIsActive);
        Assert.Equal(t, a.LastTriggeredUtc);
    }

    [Fact]
    public async Task UpdateStateAsync_inactive_keeps_triggered_time()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddAsync(10UL, serverId, 42UL, "Alarm 42", 1UL);
        var t = DateTimeOffset.Parse("2026-06-22T12:00:00Z", CultureInfo.InvariantCulture);
        await store.UpdateStateAsync(10UL, serverId, 42UL, isActive: true, triggeredUtc: t);

        await store.UpdateStateAsync(10UL, serverId, 42UL, isActive: false, triggeredUtc: null);

        var a = await store.GetAsync(10UL, serverId, 42UL);
        Assert.False(a!.LastIsActive);
        Assert.Equal(t, a.LastTriggeredUtc); // unchanged — only the active edge stamps it
    }

    [Fact]
    public async Task Remove_deletes_the_row()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddAsync(10UL, serverId, 42UL, "Alarm 42", 7UL);

        await store.RemoveAsync(10UL, serverId, 42UL);

        Assert.Null(await store.GetAsync(10UL, serverId, 42UL));
    }

    [Fact]
    public async Task Mutators_are_noops_when_alarm_absent()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        // Should not throw.
        await store.RenameAsync(10UL, serverId, 42UL, "x");
        await store.SetMessageIdAsync(10UL, serverId, 42UL, 1UL);
        await store.SetPingEveryoneAsync(10UL, serverId, 42UL, true);
        await store.SetRelayToTeamChatAsync(10UL, serverId, 42UL, true);
        await store.UpdateStateAsync(10UL, serverId, 42UL, isActive: true, triggeredUtc: DateTimeOffset.UnixEpoch);
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
        await store.AddAsync(1UL, serverId, 42UL, "Alarm 42", 7UL);

        await store.SetReachabilityAsync(1UL, serverId, 42UL, DeviceReachability.NoPrivilege);

        var a = await store.GetAsync(1UL, serverId, 42UL);
        Assert.Equal(DeviceReachability.NoPrivilege, a!.Reachability);
    }
}
