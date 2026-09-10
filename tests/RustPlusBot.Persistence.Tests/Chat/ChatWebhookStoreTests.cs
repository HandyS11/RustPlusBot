using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Persistence.Chat;

namespace RustPlusBot.Persistence.Tests.Chat;

public sealed class ChatWebhookStoreTests
{
    private const ulong Guild = 10UL;

    private const ulong Channel = 777UL;

    private const string Key = "RustPlusBot TeamChat";

    private static ICredentialProtector PrefixingProtector()
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Protect(Arg.Any<string>()).Returns(call => "enc:" + call.Arg<string>());
        protector.Unprotect(Arg.Any<string>()).Returns(call => call.Arg<string>()["enc:".Length..]);
        return protector;
    }

    [Fact]
    public async Task Save_then_Get_round_trips_the_webhook_and_stores_the_token_protected()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;
        var store = new ChatWebhookStore(context, PrefixingProtector());

        await store.SaveAsync(Guild, Channel, Key, 4242UL, "s3cret");

        var webhook = await store.GetAsync(Guild, Channel, Key);
        Assert.NotNull(webhook);
        Assert.Equal(4242UL, webhook.Value.Id);
        Assert.Equal("s3cret", webhook.Value.Token);

        // The column holds ciphertext, not the token as handed in.
        var row = await context.ChatWebhooks.SingleAsync();
        Assert.Equal("enc:s3cret", row.Token);
        Assert.Equal(Channel, row.ChannelDiscordId);
    }

    [Fact]
    public async Task Get_returns_null_when_nothing_was_recorded()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;
        var store = new ChatWebhookStore(context, PrefixingProtector());

        Assert.Null(await store.GetAsync(Guild, Channel, Key));
    }

    /// <summary>
    /// Re-recording the same channel and kind replaces the row rather than adding a second one: a
    /// re-created webhook must not leave the old id behind for the unique index to reject.
    /// </summary>
    [Fact]
    public async Task Saving_again_replaces_the_recorded_webhook()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;
        var store = new ChatWebhookStore(context, PrefixingProtector());

        await store.SaveAsync(Guild, Channel, Key, 1UL, "first");
        await store.SaveAsync(Guild, Channel, Key, 2UL, "second");

        var webhook = await store.GetAsync(Guild, Channel, Key);
        Assert.Equal(2UL, webhook!.Value.Id);
        Assert.Equal("second", webhook.Value.Token);
        Assert.Single(await context.ChatWebhooks.ToListAsync());
    }

    [Fact]
    public async Task Records_are_kept_apart_per_channel_and_per_kind()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;
        var store = new ChatWebhookStore(context, PrefixingProtector());

        await store.SaveAsync(Guild, Channel, Key, 1UL, "team");
        await store.SaveAsync(Guild, Channel, "RustPlusBot ClanChat", 2UL, "clan");
        await store.SaveAsync(Guild, 888UL, Key, 3UL, "other channel");
        await store.SaveAsync(20UL, Channel, Key, 4UL, "other guild");

        Assert.Equal(1UL, (await store.GetAsync(Guild, Channel, Key))!.Value.Id);
        Assert.Equal(2UL, (await store.GetAsync(Guild, Channel, "RustPlusBot ClanChat"))!.Value.Id);
        Assert.Equal(3UL, (await store.GetAsync(Guild, 888UL, Key))!.Value.Id);
        Assert.Equal(4UL, (await store.GetAsync(20UL, Channel, Key))!.Value.Id);
    }

    [Fact]
    public async Task Forget_drops_only_that_record()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;
        var store = new ChatWebhookStore(context, PrefixingProtector());

        await store.SaveAsync(Guild, Channel, Key, 1UL, "team");
        await store.SaveAsync(Guild, 888UL, Key, 2UL, "other channel");

        await store.ForgetAsync(Guild, Channel, Key);

        Assert.Null(await store.GetAsync(Guild, Channel, Key));
        Assert.NotNull(await store.GetAsync(Guild, 888UL, Key));
    }

    /// <summary>
    /// A token the current Data Protection key ring cannot read is dead weight — the relay can neither
    /// post through it nor repair it. Reporting "none" and dropping the row lets the next post
    /// re-resolve the webhook instead of failing every line from then on.
    /// </summary>
    [Fact]
    public async Task An_unreadable_token_reports_no_record_and_drops_it()
    {
        var (context, database) = SqliteContextFixture.Create();
        await using var _ = database;
        await using var __ = context;

        var protector = Substitute.For<ICredentialProtector>();
        protector.Protect(Arg.Any<string>()).Returns(call => call.Arg<string>());
        protector.Unprotect(Arg.Any<string>()).Returns(_ => throw new CryptographicException("key ring rotated"));
        var store = new ChatWebhookStore(context, protector);

        await store.SaveAsync(Guild, Channel, Key, 1UL, "unreadable");

        Assert.Null(await store.GetAsync(Guild, Channel, Key));
        Assert.Empty(await context.ChatWebhooks.ToListAsync());
    }

    /// <summary>
    /// The record is a guild-scoped managed resource, so it is stamped like every other row the bot
    /// owns and is swept up by the guild purge without anyone naming the table.
    /// </summary>
    [Fact]
    public async Task Recorded_webhooks_are_stamped_and_guild_scoped()
    {
        var (context, database) = SqliteContextFixture.Create(new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        await using var _ = database;
        await using var __ = context;
        var store = new ChatWebhookStore(context, PrefixingProtector());

        await store.SaveAsync(Guild, Channel, Key, 1UL, "team");

        var row = await context.ChatWebhooks.SingleAsync();
        Assert.Equal(Guild, row.GuildId);
        Assert.Equal(DateTimeOffset.UnixEpoch, row.CreatedAt);
        Assert.Equal(DateTimeOffset.UnixEpoch, row.UpdatedAt);
    }
}
