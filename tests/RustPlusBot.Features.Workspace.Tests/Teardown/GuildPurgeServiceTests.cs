using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Persistord.Testing;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.Clans;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Domain.Vending;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Teardown;

public sealed class GuildPurgeServiceTests
{
    [Fact]
    public async Task PurgeGuild_RemovesTargetGuildRows_AndLeavesOtherGuildIntact()
    {
        await using var database = SqliteTestDatabase.Private();
        await using var context = database.CreateContext<BotDbContext>(options => new BotDbContext(options));

        var serverA = new RustServer
        {
            GuildId = 1, Name = "A", Ip = "a", Port = 1
        };
        var serverB = new RustServer
        {
            GuildId = 2, Name = "B", Ip = "b", Port = 2
        };
        context.RustServers.AddRange(serverA, serverB);
        context.SmartSwitches.Add(new SmartSwitch
        {
            GuildId = 1, ServerId = serverA.Id, EntityId = 10, Name = "sw"
        });
        context.ConnectionStates.Add(new ConnectionState
        {
            RustServerId = serverA.Id, GuildId = 1, Status = ConnectionStatus.Connected
        });
        context.GuildSettings.Add(new GuildSettings
        {
            GuildId = 1, Culture = "en"
        });
        context.GuildSettings.Add(new GuildSettings
        {
            GuildId = 2, Culture = "fr"
        });
        context.FcmRegistrations.Add(new FcmRegistration
        {
            GuildId = 1, OwnerUserId = 100, ProtectedFcmCredentials = "x"
        });
        context.FcmRegistrations.Add(new FcmRegistration
        {
            GuildId = 2, OwnerUserId = 200, ProtectedFcmCredentials = "y"
        });

        // Two tables the old purge never named: one that cascaded off RustServer and one that is
        // reached only through IGuildScoped. Both go because they declare the interface.
        context.VendingGridTracks.Add(new VendingGridTrack
        {
            GuildId = 1, ServerId = serverA.Id, Grid = "D7", RegisteredBySteamId = 5
        });
        context.ClanPlayerNames.Add(new ClanPlayerName
        {
            GuildId = 1, ServerId = serverA.Id, SteamId = 5, Name = "Alice"
        });
        context.ClanPlayerNames.Add(new ClanPlayerName
        {
            GuildId = 2, ServerId = serverB.Id, SteamId = 6, Name = "Bob"
        });
        await context.SaveChangesAsync();

        // Real teardown over fake Discord I/O, sharing the lock the purge holds. An empty category set
        // makes the teardown core a no-op on channels while still proving it runs under the held lock.
        var gateway = Substitute.For<IWorkspaceGateway>();
        var store = Substitute.For<IWorkspaceStore>();
        IReadOnlyList<ProvisionedCategory> noCategories = [];
        store.GetAllCategoriesAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>())
            .Returns(noCategories);
        var provisioningLock = new ProvisioningLock();
        var teardown = new WorkspaceTeardownService(gateway, store, provisioningLock);
        var service = new GuildPurgeService(
            context, new ServerService(context), teardown, provisioningLock,
            Substitute.For<IServerConnectionStopper>());

        await service.PurgeGuildAsync(1);

        await store.Received(1).GetAllCategoriesAsync(1, Arg.Any<CancellationToken>());
        Assert.Empty(await context.RustServers.Where(s => s.GuildId == 1).ToListAsync());
        Assert.Empty(await context.SmartSwitches.ToListAsync());
        Assert.Empty(await context.ConnectionStates.ToListAsync());
        Assert.Empty(await context.GuildSettings.Where(g => g.GuildId == 1).ToListAsync());
        Assert.Empty(await context.FcmRegistrations.Where(f => f.GuildId == 1).ToListAsync());
        Assert.Empty(await context.VendingGridTracks.ToListAsync());
        Assert.Empty(await context.ClanPlayerNames.Where(n => n.GuildId == 1).ToListAsync());

        // Guild 2 untouched.
        Assert.Single(await context.RustServers.Where(s => s.GuildId == 2).ToListAsync());
        Assert.Single(await context.GuildSettings.Where(g => g.GuildId == 2).ToListAsync());
        Assert.Single(await context.FcmRegistrations.Where(f => f.GuildId == 2).ToListAsync());
        Assert.Single(await context.ClanPlayerNames.Where(n => n.GuildId == 2).ToListAsync());
    }

    /// <summary>
    /// The purge deletes each RustServer row, and the connection loop for that server may still be running.
    /// It must be stopped FIRST: a live loop whose server row has vanished faults on the foreign key the
    /// moment it writes its next status, and its socket stays open for the life of the process.
    /// </summary>
    [Fact]
    public async Task PurgeGuild_StopsEachServersConnection_WhileItsRowStillExists()
    {
        await using var database = SqliteTestDatabase.Private();
        await using var context = database.CreateContext<BotDbContext>(options => new BotDbContext(options));

        var server = new RustServer
        {
            GuildId = 1, Name = "A", Ip = "a", Port = 1
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        var gateway = Substitute.For<IWorkspaceGateway>();
        var store = Substitute.For<IWorkspaceStore>();
        IReadOnlyList<ProvisionedCategory> noCategories = [];
        store.GetAllCategoriesAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(noCategories);
        var provisioningLock = new ProvisioningLock();
        var teardown = new WorkspaceTeardownService(gateway, store, provisioningLock);

        // Records whether the server row was still present at the moment the stop was requested — the
        // ordering is the whole point, so asserting the call happened is not enough.
        var rowPresentAtStop = new List<bool>();
        var stopper = Substitute.For<IServerConnectionStopper>();
        stopper.StopAsync(Arg.Any<ulong>(), Arg.Any<Guid>()).Returns(call =>
        {
            var id = call.ArgAt<Guid>(1);
            rowPresentAtStop.Add(context.RustServers.AsNoTracking().Any(s => s.Id == id));
            return Task.CompletedTask;
        });

        var service = new GuildPurgeService(
            context, new ServerService(context), teardown, provisioningLock, stopper);

        await service.PurgeGuildAsync(1);

        await stopper.Received(1).StopAsync(1UL, server.Id);
        Assert.Equal([true], rowPresentAtStop);
    }
}
