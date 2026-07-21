using System.Collections.Concurrent;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Chat;

namespace RustPlusBot.Features.Chat.Webhooks;

/// <summary>
/// Real <see cref="IChatWebhookPoster"/>. Ensures one webhook per (channel kind, channel), named after the
/// kind (created if missing, re-discovered by name on restart) and caches the webhook client per
/// (kind, channel). Untested integration shim.
/// </summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordChatWebhookPoster(
    DiscordSocketClient client,
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
        ulong channelId,
        string username,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            var webhook = await GetOrCreateClientAsync(kind, channelId).ConfigureAwait(false);
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
    /// Webhook name per channel kind. These strings are load-bearing: the poster re-discovers its
    /// webhook by name on restart, so changing one orphans every webhook already created in live
    /// guilds and silently creates a duplicate alongside it.
    /// </summary>
    /// <param name="kind">The channel kind.</param>
    /// <returns>The webhook name to find or create.</returns>
    private static string WebhookNameFor(ChatChannelKind kind) => kind switch
    {
        ChatChannelKind.Clan => "RustPlusBot ClanChat",
        _ => "RustPlusBot TeamChat",
    };

    private async Task<DiscordWebhookClient?> GetOrCreateClientAsync(ChatChannelKind kind, ulong channelId)
    {
        if (_clients.TryGetValue((kind, channelId), out var cached))
        {
            return cached;
        }

        if (await client.GetChannelAsync(channelId).ConfigureAwait(false) is not ITextChannel channel)
        {
            return null;
        }

        var name = WebhookNameFor(kind);
        var hooks = await channel.GetWebhooksAsync().ConfigureAwait(false);
        var hook = hooks.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.Ordinal))
                   ?? await channel.CreateWebhookAsync(name).ConfigureAwait(false);
        var webhookClient = new DiscordWebhookClient(hook);
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
}
