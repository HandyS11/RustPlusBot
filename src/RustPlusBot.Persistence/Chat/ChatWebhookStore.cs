using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Persistord.Managed;
using Persistord.Managed.Entities;
using RustPlusBot.Abstractions.Credentials;

namespace RustPlusBot.Persistence.Chat;

/// <summary>
/// EF-backed <see cref="IChatWebhookStore"/> over Persistord's <see cref="ManagedWebhook"/> records,
/// scoped by channel so one channel can hold a webhook per chat kind.
/// </summary>
/// <param name="context">The bot database context.</param>
/// <param name="protector">Protects the webhook token before it is written.</param>
public sealed class ChatWebhookStore(BotDbContext context, ICredentialProtector protector) : IChatWebhookStore
{
    /// <inheritdoc />
    public async Task<ChatWebhook?> GetAsync(
        ulong guildId,
        ulong channelId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var record = await context.FindManagedAsync<ManagedWebhook>(guildId, Scope(channelId), key,
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        try
        {
            return new ChatWebhook(record.DiscordId, protector.Unprotect(record.Token));
        }
        catch (CryptographicException)
        {
            // The Data Protection key ring that wrote this token can no longer read it (a rebuilt
            // container with no persisted keys, say). The record is dead weight: drop it and report
            // "none", so the caller re-discovers the webhook and records a readable token.
            await ForgetAsync(guildId, channelId, key, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        ulong guildId,
        ulong channelId,
        string key,
        ulong webhookId,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        // The token is written already protected, by the bot's own ICredentialProtector — the same one
        // that covers player and FCM credentials. Persistord's [Protected] attribute on this column is
        // inert unless Persistord.Protection is referenced and wired; if it ever is, this call has to
        // go at the same time or every token gets encrypted twice.
        await context.UpsertManagedAsync<ManagedWebhook>(
                guildId,
                Scope(channelId),
                key,
                webhookId,
                record =>
                {
                    record.ChannelDiscordId = channelId;
                    record.Token = protector.Protect(token);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ForgetAsync(
        ulong guildId,
        ulong channelId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var scope = Scope(channelId);
        await context.ChatWebhooks
            .Where(w => w.GuildId == guildId && w.Scope == scope && w.Key == key)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // ExecuteDelete bypasses the change tracker; a record read earlier in this scope would linger.
        foreach (var entry in context.ChangeTracker.Entries<ManagedWebhook>()
                     .Where(e => e.Entity.GuildId == guildId && e.Entity.Key == key
                                                             && string.Equals(e.Entity.Scope, scope,
                                                                 StringComparison.Ordinal))
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>The managed scope a channel's webhooks live in: the channel id, as an invariant string.</summary>
    /// <param name="channelId">The channel snowflake.</param>
    /// <returns>The scope value.</returns>
    private static string Scope(ulong channelId) => channelId.ToString(CultureInfo.InvariantCulture);
}
