using System.Collections.Concurrent;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Persistence.Chat;

namespace RustPlusBot.Features.Chat.Webhooks;

/// <summary>
/// Real <see cref="IChatWebhookPoster"/>. Ensures one webhook per (channel kind, channel) and caches the
/// webhook client per (kind, channel). The webhook it creates is recorded in the database, so a restart
/// reuses the webhook the bot already owns instead of hunting for one by name. Untested integration shim.
/// </summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="scopeFactory">Opens a scope to reach the scoped <see cref="IChatWebhookStore"/>.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordChatWebhookPoster(
    DiscordSocketClient client,
    IServiceScopeFactory scopeFactory,
    ILogger<DiscordChatWebhookPoster> logger)
    : IChatWebhookPoster, IAsyncDisposable
{
    private readonly ConcurrentDictionary<(ChatChannelKind Kind, ulong ChannelId), DiscordWebhookClient> _clients =
        new();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        foreach (var c in _clients.Values)
        {
            c.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async Task PostAsync(
        ChatChannelKind kind,
        ulong guildId,
        ulong channelId,
        string username,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            var webhook = await GetOrCreateClientAsync(kind, guildId, channelId, cancellationToken)
                .ConfigureAwait(false);
            if (webhook is null)
            {
                return;
            }

            await webhook.SendMessageAsync(message, username: username, allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Broad catch: a webhook post failure must not crash the relay loop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPostFailed(logger, ex, kind, channelId);
        }
    }

    /// <summary>
    /// Webhook name per channel kind, and the key its record is stored under. The name is only a
    /// fallback now that the webhook is recorded — a guild that predates the record still has its
    /// webhook found by name once, and re-recorded — but changing one of these strings still orphans
    /// every unrecorded webhook already created in a live guild.
    /// </summary>
    /// <param name="kind">The channel kind.</param>
    /// <returns>The webhook name to find or create.</returns>
    internal static string WebhookNameFor(ChatChannelKind kind) => kind switch
    {
        ChatChannelKind.Clan => "RustPlusBot ClanChat",
        _ => "RustPlusBot TeamChat",
    };

    private async Task<DiscordWebhookClient?> GetOrCreateClientAsync(
        ChatChannelKind kind,
        ulong guildId,
        ulong channelId,
        CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue((kind, channelId), out var cached))
        {
            return cached;
        }

        var name = WebhookNameFor(kind);

        var recorded = await FromRecordAsync(kind, guildId, channelId, name, cancellationToken)
            .ConfigureAwait(false);
        if (recorded is not null)
        {
            return recorded;
        }

        if (await client.GetChannelAsync(channelId).ConfigureAwait(false) is not ITextChannel channel)
        {
            return null;
        }

        // No record yet (or the recorded one was unusable): fall back to the name lookup, which is what
        // keeps a guild provisioned by an older build from getting a second webhook, then record what we
        // end up with so this is the last time this channel is searched.
        var hooks = await channel.GetWebhooksAsync().ConfigureAwait(false);
        var hook = hooks.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.Ordinal))
                   ?? await channel.CreateWebhookAsync(name).ConfigureAwait(false);

        await SaveRecordAsync(guildId, channelId, name, hook, cancellationToken).ConfigureAwait(false);
        return Cache(kind, channelId, new DiscordWebhookClient(hook));
    }

    private async Task<DiscordWebhookClient?> FromRecordAsync(
        ChatChannelKind kind,
        ulong guildId,
        ulong channelId,
        string name,
        CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IChatWebhookStore>();
            var record = await store.GetAsync(guildId, channelId, name, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                return null;
            }

            try
            {
                return Cache(kind, channelId, new DiscordWebhookClient(record.Value.Id, record.Value.Token));
            }
#pragma warning disable CA1031 // Broad catch: any failure here means the record is unusable, whatever it was.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // The recorded webhook is gone or its token was revoked — the constructor validates it
                // against Discord. Forget it so the lookup below can adopt or create a replacement,
                // rather than failing every line from now on.
                LogRecordUnusable(logger, ex, kind, channelId);
                await store.ForgetAsync(guildId, channelId, name, cancellationToken).ConfigureAwait(false);
                return null;
            }
        }
    }

    private async Task SaveRecordAsync(
        ulong guildId,
        ulong channelId,
        string name,
        IWebhook hook,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(hook.Token))
        {
            return; // Nothing worth recording: without the token the record could not be posted through.
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IChatWebhookStore>();
            await store.SaveAsync(guildId, channelId, name, hook.Id, hook.Token, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private DiscordWebhookClient Cache(ChatChannelKind kind, ulong channelId, DiscordWebhookClient webhookClient)
    {
        var stored = _clients.GetOrAdd((kind, channelId), webhookClient);
        if (!ReferenceEquals(stored, webhookClient))
        {
            // Lost the race to cache the client (another caller cached one first); dispose the redundant one.
            webhookClient.Dispose();
        }

        return stored;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting a {Kind} chat line to channel {ChannelId} failed.")]
    private static partial void LogPostFailed(
        ILogger logger,
        Exception exception,
        ChatChannelKind kind,
        ulong channelId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The recorded {Kind} webhook for channel {ChannelId} is unusable; re-resolving it.")]
    private static partial void LogRecordUnusable(
        ILogger logger,
        Exception exception,
        ChatChannelKind kind,
        ulong channelId);
}
