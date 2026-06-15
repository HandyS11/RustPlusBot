using System.Collections.Concurrent;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Chat.Webhooks;

/// <summary>
/// Real <see cref="ITeamChatWebhookPoster"/>. Ensures one webhook named <see cref="WebhookName"/> per channel
/// (created if missing, re-discovered by name on restart) and caches the webhook client. Untested integration shim.
/// </summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordTeamChatWebhookPoster(
    DiscordSocketClient client,
    ILogger<DiscordTeamChatWebhookPoster> logger)
    : ITeamChatWebhookPoster, IAsyncDisposable
{
    private const string WebhookName = "RustPlusBot TeamChat";
    private readonly ConcurrentDictionary<ulong, DiscordWebhookClient> _clients = new();

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
    public async Task PostAsync(ulong channelId, string username, string message, CancellationToken cancellationToken)
    {
        try
        {
            var webhook = await GetOrCreateClientAsync(channelId).ConfigureAwait(false);
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
            LogPostFailed(logger, ex, channelId);
        }
    }

    private async Task<DiscordWebhookClient?> GetOrCreateClientAsync(ulong channelId)
    {
        if (_clients.TryGetValue(channelId, out var cached))
        {
            return cached;
        }

        if (await client.GetChannelAsync(channelId).ConfigureAwait(false) is not ITextChannel channel)
        {
            return null;
        }

        var hooks = await channel.GetWebhooksAsync().ConfigureAwait(false);
        var hook = hooks.FirstOrDefault(h => h.Name == WebhookName)
                   ?? await channel.CreateWebhookAsync(WebhookName).ConfigureAwait(false);
        var webhookClient = new DiscordWebhookClient(hook);
        return _clients.GetOrAdd(channelId, webhookClient);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting a team chat line to channel {ChannelId} failed.")]
    private static partial void LogPostFailed(ILogger logger, Exception exception, ulong channelId);
}
