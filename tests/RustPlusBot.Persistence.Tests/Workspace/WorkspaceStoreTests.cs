using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Persistence.Tests.Workspace;

public sealed class WorkspaceStoreTests
{
    private static WorkspaceStore NewStore(out BotDbContext context, out IDisposable cleanup)
    {
        var (ctx, connection) = SqliteContextFixture.Create();
        context = ctx;
        cleanup = connection;
        return new WorkspaceStore(ctx, new FixedClock(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task SaveCategory_IsUpsertByScope()
    {
        var store = NewStore(out _, out var cleanup);
        using var _cleanup = cleanup;

        await store.SaveCategoryAsync(new ProvisionedCategory
        {
            GuildId = 1, RustServerId = null, DiscordCategoryId = 10
        });
        await store.SaveCategoryAsync(new ProvisionedCategory
        {
            GuildId = 1, RustServerId = null, DiscordCategoryId = 20
        });

        var loaded = await store.GetCategoryAsync(1, null);
        Assert.NotNull(loaded);
        Assert.Equal(20UL, loaded.DiscordCategoryId);
    }

    [Fact]
    public async Task SaveChannel_UpsertByKey_AndScopeIsolated()
    {
        var store = NewStore(out _, out var cleanup);
        using var _cleanup = cleanup;

        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 1, RustServerId = null, ChannelKey = "information", DiscordChannelId = 5
        });
        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 1, RustServerId = null, ChannelKey = "information", DiscordChannelId = 6
        });
        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 2, RustServerId = null, ChannelKey = "information", DiscordChannelId = 7
        });

        var g1 = await store.GetChannelsAsync(1, null);
        Assert.Single(g1);
        Assert.Equal(6UL, g1[0].DiscordChannelId);
        var g2 = await store.GetChannelsAsync(2, null);
        Assert.Single(g2);
        Assert.Equal(7UL, g2[0].DiscordChannelId);
    }

    [Fact]
    public async Task SaveMessage_UpsertByKey_SetsTimestamps()
    {
        var store = NewStore(out _, out var cleanup);
        using var _cleanup = cleanup;

        await store.SaveMessageAsync(new ProvisionedMessage
        {
            GuildId = 1, MessageKey = "information.main", DiscordChannelId = 5, DiscordMessageId = 100
        });
        await store.SaveMessageAsync(new ProvisionedMessage
        {
            GuildId = 1, MessageKey = "information.main", DiscordChannelId = 5, DiscordMessageId = 101
        });

        var loaded = await store.GetMessageAsync(1, null, "information.main");
        Assert.NotNull(loaded);
        Assert.Equal(101UL, loaded.DiscordMessageId);
        Assert.Equal(DateTimeOffset.UnixEpoch, loaded.UpdatedAt);
    }

    [Fact]
    public async Task Culture_DefaultsToEn_AndRoundTrips()
    {
        var store = NewStore(out _, out var cleanup);
        using var _cleanup = cleanup;

        Assert.Equal("en", await store.GetCultureAsync(1));
        await store.SetCultureAsync(1, "fr");
        Assert.Equal("fr", await store.GetCultureAsync(1));
    }

    [Fact]
    public async Task DeleteScope_RemovesOnlyThatScope()
    {
        var store = NewStore(out var context, out var cleanup);
        using var _cleanup = cleanup;

        // RustServerId is a FK to RustServers, so we must insert a real server first.
        var server = new RustPlusBot.Domain.Servers.RustServer
        {
            GuildId = 1, Name = "S", Ip = "1.1.1.1", Port = 1
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        await store.SaveCategoryAsync(new ProvisionedCategory
        {
            GuildId = 1, RustServerId = null, DiscordCategoryId = 10
        });
        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 1, RustServerId = null, ChannelKey = "information", DiscordChannelId = 5
        });
        await store.SaveCategoryAsync(new ProvisionedCategory
        {
            GuildId = 1, RustServerId = server.Id, DiscordCategoryId = 11
        });

        await store.DeleteScopeAsync(1, null);

        Assert.Null(await store.GetCategoryAsync(1, null));
        Assert.Empty(await store.GetChannelsAsync(1, null));
        Assert.NotNull(await store.GetCategoryAsync(1, server.Id));
    }

    [Fact]
    public async Task Deleting_a_channel_also_removes_its_anchored_messages()
    {
        var store = NewStore(out _, out var cleanup);
        using var _cleanup = cleanup;

        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 1, RustServerId = null, ChannelKey = "claninfo", DiscordChannelId = 5
        });
        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 1, RustServerId = null, ChannelKey = "information", DiscordChannelId = 6
        });
        await store.SaveMessageAsync(new ProvisionedMessage
        {
            GuildId = 1, MessageKey = "clan.overview", DiscordChannelId = 5, DiscordMessageId = 100
        });
        await store.SaveMessageAsync(new ProvisionedMessage
        {
            GuildId = 1, MessageKey = "information.main", DiscordChannelId = 6, DiscordMessageId = 101
        });

        await store.DeleteChannelAsync(1, null, "claninfo");

        var channels = await store.GetChannelsAsync(1, null);
        Assert.Single(channels);
        Assert.Equal("information", channels[0].ChannelKey);
        Assert.Null(await store.GetMessageAsync(1, null, "clan.overview"));
        Assert.NotNull(await store.GetMessageAsync(1, null, "information.main"));
    }

    [Fact]
    public async Task Deleting_an_unknown_channel_is_a_no_op()
    {
        var store = NewStore(out _, out var cleanup);
        using var _cleanup = cleanup;

        await store.DeleteChannelAsync(1, null, "claninfo");

        Assert.Empty(await store.GetChannelsAsync(1, null));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
