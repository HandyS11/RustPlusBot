using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;

namespace RustPlusBot.Persistence.Tests.Workspace;

public sealed class ProvisioningSchemaTests
{
    [Fact]
    public async Task RemovingServer_CascadeDeletesItsProvisioningRows()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 1UL, Name = "S", Ip = "1.1.1.1", Port = 1
        };
        context.RustServers.Add(server);
        context.ProvisionedCategories.Add(new ProvisionedCategory
        {
            GuildId = 1UL, RustServerId = server.Id, DiscordCategoryId = 100UL, CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.ProvisionedCategories.ToListAsync());
    }

    [Fact]
    public async Task GlobalCategory_HasNullServerId_AndPersists()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        context.ProvisionedCategories.Add(new ProvisionedCategory
        {
            GuildId = 1UL, RustServerId = null, DiscordCategoryId = 200UL, CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        var loaded = await context.ProvisionedCategories.SingleAsync();
        Assert.Null(loaded.RustServerId);
        Assert.Equal(200UL, loaded.DiscordCategoryId);
    }
}
