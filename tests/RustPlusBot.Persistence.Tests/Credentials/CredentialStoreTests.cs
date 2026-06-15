using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Credentials;

namespace RustPlusBot.Persistence.Tests.Credentials;

public sealed class CredentialStoreTests
{
    private static ICredentialProtector PassThroughProtector()
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Protect(Arg.Any<string>()).Returns(call => "enc:" + call.Arg<string>());
        return protector;
    }

    private static async Task<Guid> SeedServerAsync(BotDbContext context, ulong guildId, int port = 28015)
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
    public async Task UpsertFromPairing_FirstCredentialForServer_IsActiveAndProtected()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var serverId = await SeedServerAsync(context, 10UL);
        var protector = PassThroughProtector();
        var store = new CredentialStore(context, protector);

        var id = await store.UpsertFromPairingAsync(
            new StoreCredentialRequest(10UL, serverId, 99UL, 76561198000000000UL, "raw-token"),
            markActive: true);

        var saved = await context.PlayerCredentials.SingleAsync();
        Assert.Equal(id, saved.Id);
        Assert.Equal("enc:raw-token", saved.ProtectedPlayerToken);
        Assert.Equal(CredentialStatus.Active, saved.Status);
        protector.Received(1).Protect("raw-token");
    }

    [Fact]
    public async Task UpsertFromPairing_SecondOwnerForServer_IsStandby()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var serverId = await SeedServerAsync(context, 10UL);
        var store = new CredentialStore(context, PassThroughProtector());

        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverId, 1UL, 1UL, "t1"),
            markActive: true);
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverId, 2UL, 2UL, "t2"),
            markActive: false);

        var second = await context.PlayerCredentials.SingleAsync(c => c.OwnerUserId == 2UL);
        Assert.Equal(CredentialStatus.Standby, second.Status);
        Assert.Equal(2, await store.CountForServerAsync(10UL, serverId));
    }

    [Fact]
    public async Task UpsertFromPairing_SameOwnerAgain_RefreshesTokenAndResetsInvalid()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var serverId = await SeedServerAsync(context, 10UL);
        var store = new CredentialStore(context, PassThroughProtector());

        var id = await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverId, 1UL, 1UL, "old"),
            markActive: true);
        var row = await context.PlayerCredentials.SingleAsync();
        row.Status = CredentialStatus.Invalid;
        await context.SaveChangesAsync();

        var again = await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverId, 1UL, 1UL, "new"),
            markActive: false);

        Assert.Equal(id, again);
        var saved = await context.PlayerCredentials.SingleAsync();
        Assert.Equal("enc:new", saved.ProtectedPlayerToken);
        Assert.Equal(CredentialStatus.Standby, saved.Status);
    }

    [Fact]
    public async Task CountForServer_CountsOnlyMatchingGuildAndServer()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var serverA = await SeedServerAsync(context, 10UL);
        var serverB = await SeedServerAsync(context, 10UL, port: 28016);
        var serverAGuild20 = await SeedServerAsync(context, 20UL);
        var store = new CredentialStore(context, PassThroughProtector());

        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverA, 1UL, 1UL, "t1"),
            markActive: false);
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverA, 2UL, 2UL, "t2"),
            markActive: false);
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverB, 3UL, 3UL, "t3"),
            markActive: false);
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(20UL, serverAGuild20, 4UL, 4UL, "t4"),
            markActive: false);

        Assert.Equal(2, await store.CountForServerAsync(10UL, serverA));
    }

    [Fact]
    public async Task RemoveForOwner_RemovesOwnersCredsAcrossServers_ReturnsDistinctServerIds_LeavesOthers()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var serverA = await SeedServerAsync(context, 10UL);
        var serverB = await SeedServerAsync(context, 10UL, port: 28016);
        var store = new CredentialStore(context, PassThroughProtector());

        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverA, 1UL, 1UL, "t1"), markActive: true);
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverB, 1UL, 1UL, "t2"), markActive: true);
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverA, 2UL, 2UL, "t3"),
            markActive: false);
        var serverOtherGuild = await SeedServerAsync(context, 20UL);
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(20UL, serverOtherGuild, 1UL, 1UL, "t4"),
            markActive: true);

        var affected = await store.RemoveForOwnerAsync(10UL, 1UL);

        Assert.Equal(2, affected.Count);
        Assert.Contains(serverA, affected);
        Assert.Contains(serverB, affected);
        Assert.DoesNotContain(serverOtherGuild, affected);
        var remaining = await context.PlayerCredentials.ToListAsync();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, c => c.GuildId == 10UL && c.OwnerUserId == 2UL);
        Assert.Contains(remaining, c => c.GuildId == 20UL && c.OwnerUserId == 1UL);
    }

    [Fact]
    public async Task RemoveForOwner_WhenNothingOwned_ReturnsEmpty()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        await SeedServerAsync(context, 10UL);
        var store = new CredentialStore(context, PassThroughProtector());

        var affected = await store.RemoveForOwnerAsync(10UL, 999UL);

        Assert.Empty(affected);
    }

    [Fact]
    public async Task ListServerIdsForOwner_ReturnsDistinctServers()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var serverA = await SeedServerAsync(context, 10UL);
        var serverB = await SeedServerAsync(context, 10UL, port: 28016);
        var store = new CredentialStore(context, PassThroughProtector());
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverA, 1UL, 1UL, "t1"), markActive: true);
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverB, 1UL, 1UL, "t2"), markActive: true);

        var ids = await store.ListServerIdsForOwnerAsync(10UL, 1UL);

        Assert.Equal(2, ids.Count);
        Assert.Contains(serverA, ids);
        Assert.Contains(serverB, ids);
    }
}
