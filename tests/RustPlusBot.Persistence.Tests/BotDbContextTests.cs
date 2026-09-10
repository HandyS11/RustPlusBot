using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Persistord.Core.Abstractions;
using Persistord.Testing;
using RustPlusBot.Domain.Devices;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Domain.Switches;

namespace RustPlusBot.Persistence.Tests;

public sealed class BotDbContextTests
{
    [Fact]
    public async Task GuildSettings_PreservesSuppliedSnowflakePrimaryKey()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        const ulong guildId = 1357924680135792468UL;
        context.GuildSettings.Add(new GuildSettings
        {
            GuildId = guildId, Culture = "fr",
        });
        await context.SaveChangesAsync();

        var loaded = await context.GuildSettings.SingleAsync();
        Assert.Equal(guildId, loaded.GuildId);
        Assert.Equal("fr", loaded.Culture);
    }

    [Fact]
    public void PairedDeviceEntity_IsNotAnEntityType_SoTheDeviceTablesNeverCollapseIntoOne()
    {
        var (context, database) = SqliteContextFixture.Create();
        using var _ = database;
        using var __ = context;

        // The base is code-sharing only. If it ever entered the model, EF would map SmartSwitch and
        // SmartStorageMonitor as one table-per-hierarchy table and the two device tables would merge.
        Assert.Null(context.Model.FindEntityType(typeof(PairedDeviceEntity)));

        // Not merely absent by omission: BotDbContext ignores it, so EF itself refuses to map it even if
        // someone later adds a DbSet or a navigation that targets the base.
        var designTimeModel = (IConventionModel)context.GetService<IDesignTimeModel>().Model;
        Assert.True(designTimeModel.IsIgnored(typeof(PairedDeviceEntity)));

        var smartSwitch = context.Model.FindEntityType(typeof(SmartSwitch));
        var storageMonitor = context.Model.FindEntityType(typeof(SmartStorageMonitor));
        Assert.NotNull(smartSwitch);
        Assert.NotNull(storageMonitor);
        Assert.Null(smartSwitch.BaseType);
        Assert.Null(storageMonitor.BaseType);
        Assert.NotEqual(smartSwitch.GetTableName(), storageMonitor.GetTableName());
    }

    /// <summary>
    /// Pins the shape Persistord's conventions and PurgeGuildAsync depend on: the guild key is
    /// caller-supplied and stored as a long, a device row is uniquely identified within its server,
    /// and it cascades with the server it hangs off.
    /// </summary>
    [Fact]
    public void Model_KeepsTheShapePersistordsConventionsAssume()
    {
        var (context, database) = SqliteContextFixture.Create();
        using var _ = database;
        using var __ = context;

        context.AssertSnowflakeKey<GuildSettings>();
        context.AssertUniqueIndex<SmartSwitch>(nameof(SmartSwitch.GuildId), nameof(SmartSwitch.ServerId),
            nameof(SmartSwitch.EntityId));
        context.AssertCascade<SmartSwitch, RustServer>();
    }

    /// <summary>
    /// Every mapped entity type is guild-scoped, which is what makes PurgeGuildAsync a complete
    /// teardown: a table that opted out would silently survive a guild purge.
    /// </summary>
    [Fact]
    public void EveryMappedEntity_IsGuildScoped()
    {
        var (context, database) = SqliteContextFixture.Create();
        using var _ = database;
        using var __ = context;

        var unscoped = context.Model.GetEntityTypes()
            .Where(e => !e.IsOwned())
            .Select(e => e.ClrType)
            .Where(t => !typeof(IGuildScoped).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(unscoped);
    }

    [Fact]
    public async Task RustServer_RoundTrips_WithSnowflakeGuildId()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        const ulong guildId = 1234567890123456789UL; // larger than long.MaxValue/2; exercises ulong<->long
        context.RustServers.Add(new RustServer
        {
            GuildId = guildId,
            Name = "Main",
            Ip = "127.0.0.1",
            Port = 28082,
            AddedByUserId = ulong.MaxValue,
        });
        await context.SaveChangesAsync();

        var loaded = await context.RustServers.SingleAsync();
        Assert.NotEqual(Guid.Empty, loaded.Id);
        Assert.Equal(guildId, loaded.GuildId);
        Assert.Equal(ulong.MaxValue, loaded.AddedByUserId);
        Assert.Equal("Main", loaded.Name);
    }
}
