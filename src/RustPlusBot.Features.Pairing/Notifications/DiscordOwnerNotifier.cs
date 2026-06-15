using RustPlusBot.Discord.Notifications;

namespace RustPlusBot.Features.Pairing.Notifications;

/// <summary>DMs the owner when their FCM credentials expire, via the shared <see cref="IUserDmSender"/>.</summary>
/// <param name="dmSender">Sends the DM (swallows a closed DM).</param>
internal sealed class DiscordOwnerNotifier(IUserDmSender dmSender) : IOwnerNotifier
{
    /// <inheritdoc />
    public Task NotifyCredentialsExpiredAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default) =>
        dmSender.SendAsync(
            ownerUserId,
            "Your Rust+ credentials were rejected. Reconnect your account in #setup to keep receiving pairings.",
            cancellationToken);
}
