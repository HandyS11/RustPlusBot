using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Vending;
using RustPlusBot.Persistence.Vending;

namespace RustPlusBot.Persistence.Tests;

/// <summary>Unit tests for <see cref="VendingStore"/>.</summary>
public sealed class VendingStoreTests
{
    private const string PipeDry = "69511070";

    private const string PipeAndClothDry = "-858312878,69511070";

    private static readonly ListingKey Pipe = new(69511070, false, -932201673, false);

    private static readonly DateTimeOffset Later = DateTimeOffset.UnixEpoch.AddHours(1);

    private static (VendingStore Store, BotDbContext Context, SqliteConnection Conn, IClock Clock) Create()
    {
        var (context, connection) = SqliteContextFixture.Create();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        return (new VendingStore(context, clock), context, connection, clock);
    }

    private static async Task<Guid> SeedServerAsync(BotDbContext context, ulong guildId = 10UL, int port = 28015)
    {
        var server = new RustServer
        {
            GuildId = guildId, Name = "S", Ip = "1.1.1.1", Port = port
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }

    [Fact]
    public async Task AddGrid_IsIdempotentPerCell()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        await store.AddGridAsync(10UL, serverId, "D7", 1UL);
        await store.AddGridAsync(10UL, serverId, "D7", 2UL);

        var grids = await store.ListGridsAsync(10UL, serverId);
        Assert.Equal(["D7"], grids);
    }

    [Fact]
    public async Task AddGrid_ConcurrentDuplicateLandingBetweenCheckAndSave_ReturnsRatherThanThrowing()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        // Simulates a second caller racing this one: its insert lands, behind this call's back, in the
        // exact window between AddGridAsync's own "not present" check and its SaveChangesAsync — by
        // inserting the same (guild, server, grid) row, through a second context sharing the same
        // connection, right as SaveChanges is about to hit the database. That is deterministic (no real
        // threads/timing needed) and still exercises the actual DbUpdateException path: the unique index
        // on (GuildId, ServerId, Grid) rejects this context's own insert, and AddGridAsync must recover
        // by re-querying rather than letting the exception escape.
        context.SavingChanges += (_, _) =>
        {
            var rivalOptions = new DbContextOptionsBuilder<BotDbContext>().UseSqlite(conn).Options;
            using var rival = new BotDbContext(rivalOptions);
            rival.VendingGridTracks.Add(new VendingGridTrack
            {
                GuildId = 10UL,
                ServerId = serverId,
                Grid = "D7",
                RegisteredBySteamId = 2UL,
                CreatedUtc = DateTimeOffset.UnixEpoch,
            });
            rival.SaveChanges();
        };

        await store.AddGridAsync(10UL, serverId, "D7", 1UL);

        var grids = await store.ListGridsAsync(10UL, serverId);
        Assert.Equal(["D7"], grids);
    }

    [Fact]
    public async Task UpsertListing_SecondCallRepricesRatherThanDuplicating()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        await store.UpsertListingAsync(10UL, serverId, Pipe, 1, 12, 5UL);
        await store.UpsertListingAsync(10UL, serverId, Pipe, 1, 9, 5UL);

        var listing = Assert.Single(await store.ListListingsAsync(10UL, serverId));
        Assert.Equal(9, listing.CostPerOrder);
    }

    [Fact]
    public async Task AddGrid_NormalisesTheCellBeforeStoringIt()
    {
        // Grids are matched as stored strings everywhere else in the feature, so a cell typed as
        // " d7 " and one typed as "D7" have to end up as the same row.
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        await store.AddGridAsync(10UL, serverId, " d7 ", 1UL);

        Assert.Equal(["D7"], await store.ListGridsAsync(10UL, serverId));
    }

    [Fact]
    public async Task AddGrid_FailureThatIsNotADuplicate_Propagates()
    {
        // The DbUpdateException catch exists only to absorb the idempotency race. A save that fails for
        // any other reason — here a foreign key pointing at a server that does not exist — leaves no row
        // behind, so swallowing it would report a registration that never happened.
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;

        await Assert.ThrowsAsync<DbUpdateException>(
            () => store.AddGridAsync(10UL, Guid.NewGuid(), "D7", 1UL));
    }

    [Fact]
    public async Task ListGrids_ReturnsOneServersCellsAscending()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        var otherServer = await SeedServerAsync(context, port: 28016);
        var otherGuild = await SeedServerAsync(context, guildId: 11UL);

        await store.AddGridAsync(10UL, serverId, "K12", 1UL);
        await store.AddGridAsync(10UL, serverId, "A1", 1UL);
        await store.AddGridAsync(10UL, serverId, "D7", 1UL);
        await store.AddGridAsync(10UL, otherServer, "B2", 1UL);
        await store.AddGridAsync(11UL, otherGuild, "C3", 1UL);

        // Ascending order is what !vtracked prints; unordered output would reshuffle the reply on
        // every call. The other server's and other guild's cells must not leak in.
        Assert.Equal(["A1", "D7", "K12"], await store.ListGridsAsync(10UL, serverId));
    }

    [Fact]
    public async Task RemoveGrid_TrackedCell_RemovesItAndReportsTrue()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddGridAsync(10UL, serverId, "D7", 1UL);

        Assert.True(await store.RemoveGridAsync(10UL, serverId, " d7 "));
        Assert.Empty(await store.ListGridsAsync(10UL, serverId));
    }

    [Fact]
    public async Task RemoveGrid_UntrackedCell_ReportsFalseAndLeavesTheOthersStanding()
    {
        // False is what tells !vuntrack to say "that was not tracked" rather than confirming a removal.
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.AddGridAsync(10UL, serverId, "D7", 1UL);

        Assert.False(await store.RemoveGridAsync(10UL, serverId, "A1"));
        Assert.Equal(["D7"], await store.ListGridsAsync(10UL, serverId));
    }

    [Fact]
    public async Task PurgeGrids_ClearsOneServerAndLeavesTheOtherIntact()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var wiped = await SeedServerAsync(context);
        var untouched = await SeedServerAsync(context, port: 28016);
        await store.AddGridAsync(10UL, wiped, "D7", 1UL);
        await store.AddGridAsync(10UL, wiped, "A1", 1UL);
        await store.AddGridAsync(10UL, untouched, "B2", 1UL);

        await store.PurgeGridsAsync(10UL, wiped);

        Assert.Empty(await store.ListGridsAsync(10UL, wiped));
        Assert.Equal(["B2"], await store.ListGridsAsync(10UL, untouched));
    }

    [Fact]
    public async Task UpsertListing_ClampsQuantityToAtLeastOne()
    {
        // Quantity is the divisor in every unit-price comparison the feature makes; a stored 0 would
        // divide by zero the moment the listing was ranked against a rival.
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        await store.UpsertListingAsync(10UL, serverId, Pipe, 0, 12, 5UL);

        Assert.Equal(1, Assert.Single(await store.ListListingsAsync(10UL, serverId)).Quantity);
    }

    [Fact]
    public async Task UpsertListing_Reprice_KeepsTheOriginalRegistrar()
    {
        // Repricing is not re-registering: !vtracked attributes the listing to whoever created it, and
        // rewriting that on every price change would credit the last person to touch it.
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        await store.UpsertListingAsync(10UL, serverId, Pipe, 1, 12, 5UL);
        await store.UpsertListingAsync(10UL, serverId, Pipe, 4, 40, 6UL);

        var listing = Assert.Single(await store.ListListingsAsync(10UL, serverId));
        Assert.Equal(5UL, listing.RegisteredByUserId);
        Assert.Equal(4, listing.Quantity);
        Assert.Equal(40, listing.CostPerOrder);
    }

    [Fact]
    public async Task UpsertListing_BlueprintIsADistinctListingFromTheItem()
    {
        // Same item id, same currency: only the blueprint flag separates a 5-scrap blueprint from a
        // 5-scrap item. Collapsing them would have one silently overwrite the other's price.
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        var blueprint = Pipe with
        {
            ItemIsBlueprint = true
        };

        await store.UpsertListingAsync(10UL, serverId, Pipe, 1, 12, 5UL);
        await store.UpsertListingAsync(10UL, serverId, blueprint, 1, 40, 5UL);

        var listings = await store.ListListingsAsync(10UL, serverId);
        Assert.Equal(2, listings.Count);
        Assert.Equal(12, Assert.Single(listings, l => !l.ItemIsBlueprint).CostPerOrder);
        Assert.Equal(40, Assert.Single(listings, l => l.ItemIsBlueprint).CostPerOrder);
    }

    [Fact]
    public async Task ListListings_ReturnsOnlyTheServersOwnRows()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        var otherServer = await SeedServerAsync(context, port: 28016);

        await store.UpsertListingAsync(10UL, serverId, Pipe, 1, 12, 5UL);
        await store.UpsertListingAsync(10UL, otherServer, Pipe, 1, 99, 5UL);

        Assert.Equal(12, Assert.Single(await store.ListListingsAsync(10UL, serverId)).CostPerOrder);
    }

    [Fact]
    public async Task RemoveListing_Registered_RemovesItAndReportsTrue()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.UpsertListingAsync(10UL, serverId, Pipe, 1, 12, 5UL);

        Assert.True(await store.RemoveListingAsync(10UL, serverId, Pipe));
        Assert.Empty(await store.ListListingsAsync(10UL, serverId));
    }

    [Fact]
    public async Task RemoveListing_NotRegistered_ReportsFalse()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        Assert.False(await store.RemoveListingAsync(10UL, serverId, Pipe));
    }

    [Fact]
    public async Task UpsertNotification_FirstCall_StoresTheMessageAndTheTimeItWasPosted()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        var otherServer = await SeedServerAsync(context, port: 28016);

        await store.UpsertNotificationAsync(10UL, serverId, Pipe, 555UL, 1, 10);

        var row = Assert.Single(await store.ListNotificationsAsync(10UL, serverId));
        Assert.Equal(555UL, row.MessageId);
        Assert.Equal(1, row.ReferenceQuantity);
        Assert.Equal(10, row.ReferenceCostPerOrder);
        Assert.Equal(DateTimeOffset.UnixEpoch, row.PostedUtc);
        Assert.Empty(await store.ListNotificationsAsync(10UL, otherServer));
    }

    [Fact]
    public async Task UpsertNotification_NothingChanged_WritesNothingAtAll()
    {
        // The relay reconciles every five seconds and re-upserts every live notice. Without the
        // unchanged-guard that is a SaveChanges per notice per poll, forever, and PostedUtc would be
        // rewritten each time so the column permanently read "just now".
        var (store, context, conn, clock) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.UpsertNotificationAsync(10UL, serverId, Pipe, 555UL, 1, 10);

        var saves = 0;
        context.SavingChanges += (_, _) => saves++;
        clock.UtcNow.Returns(Later);

        await store.UpsertNotificationAsync(10UL, serverId, Pipe, 555UL, 1, 10);

        Assert.Equal(0, saves);
        Assert.Equal(DateTimeOffset.UnixEpoch,
            Assert.Single(await store.ListNotificationsAsync(10UL, serverId)).PostedUtc);
    }

    [Fact]
    public async Task UpsertNotification_NewMessageId_MovesPostedUtc()
    {
        // A different id means the message was genuinely reposted, which is exactly what PostedUtc
        // is supposed to record.
        var (store, context, conn, clock) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.UpsertNotificationAsync(10UL, serverId, Pipe, 555UL, 1, 10);

        clock.UtcNow.Returns(Later);
        await store.UpsertNotificationAsync(10UL, serverId, Pipe, 556UL, 1, 10);

        var row = Assert.Single(await store.ListNotificationsAsync(10UL, serverId));
        Assert.Equal(556UL, row.MessageId);
        Assert.Equal(Later, row.PostedUtc);
    }

    [Fact]
    public async Task UpsertNotification_SameMessageRepriced_UpdatesTheReferenceButNotPostedUtc()
    {
        // The same message edited in place was not reposted, so its "posted" time must not move —
        // an unconditional rewrite would make every live notice read as brand new on every poll.
        var (store, context, conn, clock) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.UpsertNotificationAsync(10UL, serverId, Pipe, 555UL, 1, 10);

        clock.UtcNow.Returns(Later);
        await store.UpsertNotificationAsync(10UL, serverId, Pipe, 555UL, 2, 18);

        var row = Assert.Single(await store.ListNotificationsAsync(10UL, serverId));
        Assert.Equal(2, row.ReferenceQuantity);
        Assert.Equal(18, row.ReferenceCostPerOrder);
        Assert.Equal(DateTimeOffset.UnixEpoch, row.PostedUtc);
    }

    [Fact]
    public async Task RemoveNotification_Posted_RemovesItAndReportsTrue()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.UpsertNotificationAsync(10UL, serverId, Pipe, 555UL, 1, 10);

        Assert.True(await store.RemoveNotificationAsync(10UL, serverId, Pipe));
        Assert.Empty(await store.ListNotificationsAsync(10UL, serverId));
    }

    [Fact]
    public async Task RemoveNotification_NothingPosted_ReportsFalse()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        Assert.False(await store.RemoveNotificationAsync(10UL, serverId, Pipe));
    }

    [Fact]
    public async Task UpsertStockNotification_NullSignature_Throws()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.UpsertStockNotificationAsync(10UL, serverId, 1UL, 777UL, null!));
    }

    [Fact]
    public async Task UpsertStockNotification_FirstCall_StoresTheMessageAndTheTimeItWasPosted()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        var otherServer = await SeedServerAsync(context, port: 28016);

        await store.UpsertStockNotificationAsync(10UL, serverId, 1UL, 777UL, PipeDry);

        var row = Assert.Single(await store.ListStockNotificationsAsync(10UL, serverId));
        Assert.Equal(1UL, row.MachineId);
        Assert.Equal(777UL, row.MessageId);
        Assert.Equal(PipeDry, row.SoldOutSignature);
        Assert.Equal(DateTimeOffset.UnixEpoch, row.PostedUtc);
        Assert.Empty(await store.ListStockNotificationsAsync(10UL, otherServer));
    }

    [Fact]
    public async Task UpsertStockNotification_NothingChanged_WritesNothingAtAll()
    {
        var (store, context, conn, clock) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.UpsertStockNotificationAsync(10UL, serverId, 1UL, 777UL, PipeDry);

        var saves = 0;
        context.SavingChanges += (_, _) => saves++;
        clock.UtcNow.Returns(Later);

        await store.UpsertStockNotificationAsync(10UL, serverId, 1UL, 777UL, PipeDry);

        Assert.Equal(0, saves);
        Assert.Equal(DateTimeOffset.UnixEpoch,
            Assert.Single(await store.ListStockNotificationsAsync(10UL, serverId)).PostedUtc);
    }

    [Fact]
    public async Task UpsertStockNotification_NewMessageId_MovesPostedUtc()
    {
        var (store, context, conn, clock) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.UpsertStockNotificationAsync(10UL, serverId, 1UL, 777UL, PipeDry);

        clock.UtcNow.Returns(Later);
        await store.UpsertStockNotificationAsync(10UL, serverId, 1UL, 778UL, PipeDry);

        var row = Assert.Single(await store.ListStockNotificationsAsync(10UL, serverId));
        Assert.Equal(778UL, row.MessageId);
        Assert.Equal(Later, row.PostedUtc);
    }

    [Fact]
    public async Task UpsertStockNotification_SameMessageNewSignature_UpdatesTheSignatureButNotPostedUtc()
    {
        var (store, context, conn, clock) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.UpsertStockNotificationAsync(10UL, serverId, 1UL, 777UL, PipeDry);

        clock.UtcNow.Returns(Later);
        await store.UpsertStockNotificationAsync(10UL, serverId, 1UL, 777UL, PipeAndClothDry);

        var row = Assert.Single(await store.ListStockNotificationsAsync(10UL, serverId));
        Assert.Equal(PipeAndClothDry, row.SoldOutSignature);
        Assert.Equal(DateTimeOffset.UnixEpoch, row.PostedUtc);
    }

    [Fact]
    public async Task RemoveStockNotification_Posted_RemovesItAndReportsTrue()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        await store.UpsertStockNotificationAsync(10UL, serverId, 1UL, 777UL, PipeDry);
        await store.UpsertStockNotificationAsync(10UL, serverId, 2UL, 778UL, PipeDry);

        Assert.True(await store.RemoveStockNotificationAsync(10UL, serverId, 1UL));

        // Machines are tracked one row each; clearing one shop's warning must not clear the other's.
        Assert.Equal(2UL, Assert.Single(await store.ListStockNotificationsAsync(10UL, serverId)).MachineId);
    }

    [Fact]
    public async Task RemoveStockNotification_NothingPosted_ReportsFalse()
    {
        var (store, context, conn, _) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        Assert.False(await store.RemoveStockNotificationAsync(10UL, serverId, 1UL));
    }
}
