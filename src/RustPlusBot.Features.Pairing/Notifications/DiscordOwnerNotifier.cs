using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Pairing.Notifications;

/// <summary>DMs the owner. A closed-DM failure is swallowed (logged): the Expired state still shows in #setup.</summary>
/// <param name="client">The socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordOwnerNotifier(DiscordSocketClient client, ILogger<DiscordOwnerNotifier> logger)
    : IOwnerNotifier
{
    /// <inheritdoc />
    public async Task NotifyCredentialsExpiredAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await client.GetUserAsync(ownerUserId).ConfigureAwait(false);
            if (user is null)
            {
                LogUserNotFound(logger, ownerUserId);
                return;
            }

            await user.SendMessageAsync(
                    "Your Rust+ credentials were rejected. Reconnect your account in #setup to keep receiving pairings.")
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Broad catch is intentional: a closed DM (or any send failure) must not break the supervisor.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDmFailed(logger, ex, ownerUserId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot DM owner {OwnerId}: user not found.")]
    private static partial void LogUserNotFound(ILogger logger, ulong ownerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to DM owner {OwnerId} about expired credentials.")]
    private static partial void LogDmFailed(ILogger logger, Exception ex, ulong ownerId);
}
