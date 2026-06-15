using RustPlusBot.Features.Pairing.Listening;

namespace RustPlusBot.Features.Pairing.Supervisor;

/// <summary>Owns the live FCM listeners — one per active registration.</summary>
internal interface IPairingSupervisor
{
    /// <summary>
    /// Starts (or restarts) the listener for (guild, owner) and returns the initial connect outcome.
    /// On <see cref="PairingConnectOutcome.Rejected"/> the registration is marked Expired and the owner notified.
    /// </summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The initial connect outcome.</returns>
    Task<PairingConnectOutcome> EnsureListenerAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Starts listeners for every Active registration (called once at startup).</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StartAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Cancels and disposes every listener (called on shutdown).</summary>
    Task StopAllAsync();
}
