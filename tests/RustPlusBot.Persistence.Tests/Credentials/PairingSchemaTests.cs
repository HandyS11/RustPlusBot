using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Tests.Credentials;

public sealed class PairingSchemaTests
{
    [Fact]
    public async Task RemovingServer_CascadeDeletesItsCredentials()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        context.PlayerCredentials.Add(new PlayerCredential
        {
            GuildId = 10UL,
            RustServerId = server.Id,
            OwnerUserId = 1UL,
            SteamId = 1UL,
            ProtectedPlayerToken = "x",
            Status = CredentialStatus.Active
        });
        await context.SaveChangesAsync();

        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.PlayerCredentials.ToListAsync());
    }

    [Fact]
    public async Task DuplicateServerEndpoint_ViolatesUniqueIndex()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        context.RustServers.Add(new RustServer
        {
            GuildId = 10UL, Name = "A", Ip = "1.1.1.1", Port = 28015
        });
        context.RustServers.Add(new RustServer
        {
            GuildId = 10UL, Name = "B", Ip = "1.1.1.1", Port = 28015
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateRegistrationForOwner_ViolatesUniqueIndex()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        context.FcmRegistrations.Add(new FcmRegistration
        {
            GuildId = 10UL, OwnerUserId = 1UL, ProtectedFcmCredentials = "a"
        });
        context.FcmRegistrations.Add(new FcmRegistration
        {
            GuildId = 10UL, OwnerUserId = 1UL, ProtectedFcmCredentials = "b"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
