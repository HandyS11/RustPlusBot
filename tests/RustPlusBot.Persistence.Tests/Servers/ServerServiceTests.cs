using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Persistence.Tests.Servers;

public sealed class ServerServiceTests
{
    [Fact]
    public async Task AddAsync_PersistsServerScopedToGuild()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);

        var server = await service.AddAsync(10UL, 99UL, "Main", "127.0.0.1", 28082);

        Assert.NotEqual(Guid.Empty, server.Id);
        var listed = await service.ListAsync(10UL);
        Assert.Single(listed);
        Assert.Equal("Main", listed[0].Name);
    }

    [Fact]
    public async Task ListAsync_DoesNotLeakAcrossGuilds()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);

        await service.AddAsync(10UL, 1UL, "A", "1.1.1.1", 1);
        await service.AddAsync(20UL, 1UL, "B", "2.2.2.2", 2);

        var guild10 = await service.ListAsync(10UL);
        Assert.Single(guild10);
        Assert.Equal("A", guild10[0].Name);
    }

    [Fact]
    public async Task RemoveAsync_OnlyRemovesWithinGuild()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);

        var server = await service.AddAsync(10UL, 1UL, "A", "1.1.1.1", 1);

        Assert.False(await service.RemoveAsync(20UL, server.Id)); // wrong guild
        Assert.True(await service.RemoveAsync(10UL, server.Id));
        Assert.Empty(await service.ListAsync(10UL));
    }

    [Fact]
    public async Task ResolveOrCreateByEndpoint_CreatesWhenNew()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);

        var (server, created) = await service.ResolveOrCreateByEndpointAsync(10UL, 99UL, "Main", "1.2.3.4", 28015);

        Assert.True(created);
        Assert.NotEqual(Guid.Empty, server.Id);
        Assert.Single(await service.ListAsync(10UL));
    }

    [Fact]
    public async Task ResolveOrCreateByEndpoint_ReturnsExistingForSameEndpoint()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);

        var (Server, Created) = await service.ResolveOrCreateByEndpointAsync(10UL, 1UL, "Main", "1.2.3.4", 28015);
        var second = await service.ResolveOrCreateByEndpointAsync(10UL, 2UL, "Main again", "1.2.3.4", 28015);

        Assert.True(Created);
        Assert.False(second.Created);
        Assert.Equal(Server.Id, second.Server.Id);
        Assert.Single(await service.ListAsync(10UL));
    }

    [Fact]
    public async Task ResolveOrCreateByEndpoint_DifferentPortIsDistinct()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);

        await service.ResolveOrCreateByEndpointAsync(10UL, 1UL, "A", "1.2.3.4", 28015);
        await service.ResolveOrCreateByEndpointAsync(10UL, 1UL, "B", "1.2.3.4", 28016);

        Assert.Equal(2, (await service.ListAsync(10UL)).Count);
    }

    [Fact]
    public async Task GetByEndpoint_returns_existing_server()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);
        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        var found = await service.GetByEndpointAsync(10UL, "1.1.1.1", 28015);

        Assert.NotNull(found);
        Assert.Equal(server.Id, found.Id);
    }

    [Fact]
    public async Task GetByEndpoint_returns_null_for_unknown_endpoint()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);

        Assert.Null(await service.GetByEndpointAsync(10UL, "9.9.9.9", 28015));
    }

    [Fact]
    public async Task GetByFacepunchServerId_returns_matching_server()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);
        var fp = Guid.NewGuid();
        var server = new RustServer
        {
            GuildId = 10UL,
            Name = "S",
            Ip = "1.1.1.1",
            Port = 28015,
            FacepunchServerId = fp,
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        var found = await service.GetByFacepunchServerIdAsync(10UL, fp);

        Assert.NotNull(found);
        Assert.Equal(server.Id, found.Id);
        Assert.Null(await service.GetByFacepunchServerIdAsync(10UL, Guid.NewGuid()));
    }

    [Fact]
    public async Task SetFacepunchServerId_backfills_then_is_idempotent()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);
        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015,
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        var g = Guid.NewGuid();

        await service.SetFacepunchServerIdAsync(server.Id, g);
        var reloaded = await service.GetAsync(10UL, server.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(g, reloaded.FacepunchServerId);

        // Calling again with the same GUID is a no-op (no throw, value unchanged).
        await service.SetFacepunchServerIdAsync(server.Id, g);
        reloaded = await service.GetAsync(10UL, server.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(g, reloaded.FacepunchServerId);

        // An absent server id is a silent no-op (no throw).
        await service.SetFacepunchServerIdAsync(Guid.NewGuid(), g);
    }
}
