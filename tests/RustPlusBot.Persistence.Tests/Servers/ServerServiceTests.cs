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
}
