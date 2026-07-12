namespace RustPlusBot.Features.Pairing.Notifications;

/// <summary>Notifies a credential owner out-of-band (e.g. by DM) when action is needed.</summary>
internal interface IOwnerNotifier
{
    /// <summary>Tells the owner their FCM credentials expired and to reconnect.</summary>
    /// <param name="guildId">The guild the registration belongs to.</param>
    /// <param name="ownerUserId">The Discord user to notify.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task NotifyCredentialsExpiredAsync(ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Tells the owner a server pairing arrived but no #setup channel exists to prompt in.</summary>
    /// <param name="guildId">The guild the pairing belongs to.</param>
    /// <param name="ownerUserId">The Discord user to notify.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task NotifySetupChannelMissingAsync(ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default);
}
