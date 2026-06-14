using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Domain.Credentials;
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

    [Fact]
    public async Task StoreAsync_ProtectsTokenAndPersistsAsStandby()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var protector = PassThroughProtector();
        var store = new CredentialStore(context, protector);
        var serverId = Guid.NewGuid();

        var id = await store.StoreAsync(new StoreCredentialRequest(
            GuildId: 10UL,
            RustServerId: serverId,
            OwnerUserId: 99UL,
            SteamId: 76561198000000000UL,
            PlayerToken: "raw-token",
            FcmCredentialsJson: "{}"));

        var saved = await context.PlayerCredentials.SingleAsync();
        Assert.Equal(id, saved.Id);
        Assert.Equal("enc:raw-token", saved.ProtectedPlayerToken);
        Assert.Equal("enc:{}", saved.ProtectedFcmCredentials);
        Assert.Equal(CredentialStatus.Standby, saved.Status);

        // Both secret fields must be protected before persistence (security invariant).
        protector.Received(1).Protect("raw-token");
        protector.Received(1).Protect("{}");
    }

    [Fact]
    public async Task CountForServerAsync_CountsOnlyMatchingGuildAndServer()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var store = new CredentialStore(context, PassThroughProtector());
        var serverA = Guid.NewGuid();
        var serverB = Guid.NewGuid();

        await store.StoreAsync(new StoreCredentialRequest(10UL, serverA, 1UL, 1UL, "t1", "{}"));
        await store.StoreAsync(new StoreCredentialRequest(10UL, serverA, 2UL, 2UL, "t2", "{}"));
        await store.StoreAsync(new StoreCredentialRequest(10UL, serverB, 3UL, 3UL, "t3", "{}"));
        await store.StoreAsync(new StoreCredentialRequest(20UL, serverA, 4UL, 4UL, "t4", "{}"));

        Assert.Equal(2, await store.CountForServerAsync(10UL, serverA));
    }
}
