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
            new StoreCredentialRequest(10UL, serverId, 99UL, 76561198000000000UL, "raw-token"));

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

        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverId, 1UL, 1UL, "t1"));
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverId, 2UL, 2UL, "t2"));

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

        var id = await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverId, 1UL, 1UL, "old"));
        var row = await context.PlayerCredentials.SingleAsync();
        row.Status = CredentialStatus.Invalid;
        await context.SaveChangesAsync();

        var again = await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverId, 1UL, 1UL, "new"));

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

        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverA, 1UL, 1UL, "t1"));
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverA, 2UL, 2UL, "t2"));
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverB, 3UL, 3UL, "t3"));
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(20UL, serverAGuild20, 4UL, 4UL, "t4"));

        Assert.Equal(2, await store.CountForServerAsync(10UL, serverA));
    }
}
