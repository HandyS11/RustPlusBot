using Microsoft.EntityFrameworkCore;
using Persistord.Testing;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.Clans;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Persistence.Tests;

/// <summary>Unit tests for <see cref="ClanStore"/>.</summary>
public sealed class ClanStoreTests
{
    private static (ClanStore Store, BotDbContext Context, SqliteTestDatabase Db, FixedTimeProvider Time) Create()
    {
        var time = new FixedTimeProvider(DateTimeOffset.UnixEpoch);
        var (context, database) = SqliteContextFixture.Create(time);
        return (new ClanStore(context, time), context, database, time);
    }

    private static async Task<Guid> SeedServerAsync(BotDbContext context, ulong guildId = 10UL)
    {
        var server = new RustServer
        {
            GuildId = guildId, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }

    private static ClanSnapshot CreateSnapshot() =>
        new(
            ClanId: 42L,
            Name: "The Clan",
            Created: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Creator: 111UL,
            Motd: "Welcome",
            MotdTimestamp: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            MotdAuthor: 111UL,
            LogoHash: "abc123",
            Color: unchecked((int)0xFF00FF00),
            MaxMemberCount: 50,
            Score: 12345L,
            Roles:
            [
                new ClanRoleSnapshot(0, 0, "Leader", true, true, true, true, true, true, true, true, true),
                new ClanRoleSnapshot(1, 1, "Member", false, false, false, false, false, false, false, false, false),
            ],
            Members:
            [
                new ClanMemberSnapshot(
                    111UL,
                    0,
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero),
                    null,
                    false),
                new ClanMemberSnapshot(
                    222UL,
                    1,
                    new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero),
                    "Trusted officer",
                    true),
                new ClanMemberSnapshot(
                    333UL,
                    1,
                    new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 1, 7, 0, 0, 0, TimeSpan.Zero),
                    null,
                    true),
            ],
            Invites:
            [
                new ClanInviteSnapshot(444UL, 111UL, new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero)),
            ]);

    [Fact]
    public async Task Returns_null_when_no_clan_is_stored()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        var result = await store.GetAsync(10UL, serverId);

        Assert.Null(result);
    }

    [Fact]
    public async Task Round_trips_a_snapshot_including_members_roles_and_invites()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        var snapshot = CreateSnapshot();

        await store.SaveAsync(10UL, serverId, snapshot);
        var result = await store.GetAsync(10UL, serverId);

        Assert.NotNull(result);
        Assert.Equal(snapshot.ClanId, result.ClanId);
        Assert.Equal(snapshot.Name, result.Name);
        Assert.Equal(snapshot.Created, result.Created);
        Assert.Equal(snapshot.Creator, result.Creator);
        Assert.Equal(snapshot.Motd, result.Motd);
        Assert.Equal(snapshot.MotdTimestamp, result.MotdTimestamp);
        Assert.Equal(snapshot.MotdAuthor, result.MotdAuthor);
        Assert.Equal(snapshot.LogoHash, result.LogoHash);
        Assert.Equal(snapshot.Color, result.Color);
        Assert.Equal(snapshot.MaxMemberCount, result.MaxMemberCount);
        Assert.Equal(snapshot.Score, result.Score);

        Assert.Equal(snapshot.Roles.Count, result.Roles.Count);
        for (var i = 0; i < snapshot.Roles.Count; i++)
        {
            Assert.Equal(snapshot.Roles[i], result.Roles[i]);
        }

        Assert.Equal(snapshot.Members.Count, result.Members.Count);
        for (var i = 0; i < snapshot.Members.Count; i++)
        {
            Assert.Equal(snapshot.Members[i], result.Members[i]);
        }

        Assert.Equal(snapshot.Invites.Count, result.Invites.Count);
        for (var i = 0; i < snapshot.Invites.Count; i++)
        {
            Assert.Equal(snapshot.Invites[i], result.Invites[i]);
        }
    }

    [Fact]
    public async Task Overwrites_the_previous_snapshot_on_save()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.SaveAsync(10UL, serverId, CreateSnapshot());

        var updated = CreateSnapshot() with
        {
            Name = "Renamed Clan", Score = 99999L
        };
        await store.SaveAsync(10UL, serverId, updated);
        var result = await store.GetAsync(10UL, serverId);

        Assert.NotNull(result);
        Assert.Equal("Renamed Clan", result.Name);
        Assert.Equal(99999L, result.Score);
        Assert.Single(await context.ClanStates.ToListAsync());
    }

    [Fact]
    public async Task HasClan_is_false_before_save_and_true_after()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        Assert.False(await store.HasClanAsync(10UL, serverId));

        await store.SaveAsync(10UL, serverId, CreateSnapshot());

        Assert.True(await store.HasClanAsync(10UL, serverId));
    }

    [Fact]
    public async Task Clear_removes_the_row_and_reports_true_only_the_first_time()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.SaveAsync(10UL, serverId, CreateSnapshot());

        var first = await store.ClearAsync(10UL, serverId);
        var second = await store.ClearAsync(10UL, serverId);

        Assert.True(first);
        Assert.False(second);
        Assert.False(await store.HasClanAsync(10UL, serverId));
    }

    [Fact]
    public async Task Deleting_the_server_cascades_to_the_clan_state()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.SaveAsync(10UL, serverId, CreateSnapshot());

        var server = await context.RustServers.SingleAsync(s => s.Id == serverId);
        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.ClanStates.ToListAsync());
    }

    [Fact]
    public async Task Records_and_reads_back_player_names()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        await store.RecordNameAsync(10UL, serverId, 111UL, "Alice");

        var names = await store.GetNamesAsync(10UL, serverId, [111UL]);

        Assert.Equal("Alice", names[111UL]);
    }

    [Fact]
    public async Task Name_lookup_returns_only_the_requested_ids()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.RecordNameAsync(10UL, serverId, 111UL, "Alice");
        await store.RecordNameAsync(10UL, serverId, 222UL, "Bob");
        await store.RecordNameAsync(10UL, serverId, 333UL, "Carol");

        var names = await store.GetNamesAsync(10UL, serverId, [111UL, 333UL]);

        Assert.Equal(2, names.Count);
        Assert.Equal("Alice", names[111UL]);
        Assert.Equal("Carol", names[333UL]);
        Assert.False(names.ContainsKey(222UL));
    }

    [Fact]
    public async Task Name_lookup_returns_empty_for_no_ids()
    {
        // Note: an empty `IN ()` clause returns no rows whether or not the empty-collection guard
        // exists in GetNamesAsync, so this cannot prove the guard is what produced the empty result.
        // Seeding a row for the same server at least confirms the empty result isn't a side effect of
        // an empty table.
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.RecordNameAsync(10UL, serverId, 111UL, "Alice");

        var names = await store.GetNamesAsync(10UL, serverId, []);

        Assert.Empty(names);
    }

    [Fact]
    public async Task Recording_a_name_twice_updates_rather_than_duplicating()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        await store.RecordNameAsync(10UL, serverId, 111UL, "Alice");
        await store.RecordNameAsync(10UL, serverId, 111UL, "Alicia");

        var names = await store.GetNamesAsync(10UL, serverId, [111UL]);
        Assert.Equal("Alicia", names[111UL]);
        Assert.Single(await context.ClanPlayerNames.ToListAsync());
    }

    [Fact]
    public async Task Recording_the_same_name_again_skips_the_write()
    {
        var (store, context, conn, time) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        await store.RecordNameAsync(10UL, serverId, 111UL, "Alice");
        var firstUpdatedAt = (await context.ClanPlayerNames.SingleAsync(n => n.SteamId == 111UL)).UpdatedAt;

        time.Now = DateTimeOffset.UnixEpoch.AddMinutes(5);
        await store.RecordNameAsync(10UL, serverId, 111UL, "Alice");

        var row = await context.ClanPlayerNames.SingleAsync(n => n.SteamId == 111UL);
        Assert.Equal(firstUpdatedAt, row.UpdatedAt);
        Assert.Single(await context.ClanPlayerNames.ToListAsync());
    }

    [Fact]
    public async Task Name_lookup_ignores_a_row_whose_guild_id_belongs_to_another_guild()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        context.ClanPlayerNames.Add(new ClanPlayerName
        {
            GuildId = 999UL,
            ServerId = serverId,
            SteamId = 111UL,
            Name = "Alice",
            UpdatedAt = DateTimeOffset.UnixEpoch
        });
        await context.SaveChangesAsync();

        var names = await store.GetNamesAsync(10UL, serverId, [111UL]);

        Assert.Empty(names);
    }

    [Fact]
    public async Task Recording_a_name_heals_a_stale_guild_id_even_when_the_name_is_unchanged()
    {
        var (store, context, conn, time) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        context.ClanPlayerNames.Add(new ClanPlayerName
        {
            GuildId = 999UL,
            ServerId = serverId,
            SteamId = 111UL,
            Name = "Alice",
            UpdatedAt = DateTimeOffset.UnixEpoch
        });
        await context.SaveChangesAsync();

        time.Now = DateTimeOffset.UnixEpoch.AddMinutes(5);
        await store.RecordNameAsync(10UL, serverId, 111UL, "Alice");

        var row = await context.ClanPlayerNames.SingleAsync(n => n.SteamId == 111UL);
        Assert.Equal(10UL, row.GuildId);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(5), row.UpdatedAt);
        Assert.Single(await context.ClanPlayerNames.ToListAsync());
    }

    [Fact]
    public async Task Saving_a_clan_state_heals_a_stale_guild_id_instead_of_throwing()
    {
        var (store, context, conn, time) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        context.ClanStates.Add(new ClanState
        {
            GuildId = 999UL, ServerId = serverId, LastSeenUtc = DateTimeOffset.UnixEpoch
        });
        await context.SaveChangesAsync();

        time.Now = DateTimeOffset.UnixEpoch.AddMinutes(5);
        await store.SaveAsync(10UL, serverId, CreateSnapshot());

        var row = await context.ClanStates.SingleAsync(s => s.ServerId == serverId);
        Assert.Equal(10UL, row.GuildId);
        Assert.Equal(CreateSnapshot().Name, row.Name);
        Assert.Single(await context.ClanStates.ToListAsync());
    }
}
