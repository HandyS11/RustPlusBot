using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Tests.Servers;

/// <summary>Round-trips the wipe-baseline columns and the wipe-ping guild flag through the migrated schema.</summary>
public sealed class RustServerWipeColumnsTests
{
    [Fact]
    public async Task Wipe_baseline_columns_round_trip()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 10UL,
            Name = "S",
            Ip = "1.1.1.1",
            Port = 28015,
            LastWipeTimeUtc = new DateTimeOffset(2026, 7, 2, 18, 0, 0, TimeSpan.Zero),
            LastMapSeed = 123456u,
            LastMapSize = 4250u,
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var loaded = await context.RustServers.FindAsync(server.Id);

        Assert.NotNull(loaded);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 18, 0, 0, TimeSpan.Zero), loaded.LastWipeTimeUtc);
        Assert.Equal(123456u, loaded.LastMapSeed);
        Assert.Equal(4250u, loaded.LastMapSize);
    }

    [Fact]
    public async Task New_server_has_empty_baseline_and_guild_ping_defaults_false()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        context.GuildSettings.Add(new GuildSettings
        {
            GuildId = 10UL
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var loadedServer = await context.RustServers.FindAsync(server.Id);
        var loadedGuild = await context.GuildSettings.SingleOrDefaultAsync(g => g.GuildId == 10UL);

        Assert.NotNull(loadedServer);
        Assert.Null(loadedServer.LastWipeTimeUtc);
        Assert.Null(loadedServer.LastMapSeed);
        Assert.Null(loadedServer.LastMapSize);
        Assert.NotNull(loadedGuild);
        Assert.False(loadedGuild.PingEveryoneOnWipe);
    }
}
