using Microsoft.Data.Sqlite;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Vending;

namespace RustPlusBot.Persistence.Tests;

/// <summary>Unit tests for <see cref="VendingStore"/>.</summary>
public sealed class VendingStoreTests
{
    private static readonly ListingKey Pipe = new(69511070, false, -932201673, false);

    private static (VendingStore Store, BotDbContext Context, SqliteConnection Conn, IClock Clock) Create()
    {
        var (context, connection) = SqliteContextFixture.Create();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        return (new VendingStore(context, clock), context, connection, clock);
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
}
