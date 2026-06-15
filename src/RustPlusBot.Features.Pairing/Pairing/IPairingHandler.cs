using RustPlusBot.Features.Pairing.Listening;

namespace RustPlusBot.Features.Pairing.Pairing;

/// <summary>Turns a pairing notification into a registered server and a pooled credential.</summary>
internal interface IPairingHandler
{
    /// <summary>Handles one notification for a given owner.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user whose listener received it.</param>
    /// <param name="notification">The pairing notification.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task HandleAsync(
        ulong guildId,
        ulong ownerUserId,
        PairingNotification notification,
        CancellationToken cancellationToken);
}
