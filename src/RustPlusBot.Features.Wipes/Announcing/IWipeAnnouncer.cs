using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Features.Wipes.Announcing;

/// <summary>Posts the wipe announcement embed to the wiped server's #events channel.</summary>
internal interface IWipeAnnouncer
{
    /// <summary>Handles one <see cref="ServerWipedEvent"/>.</summary>
    /// <param name="evt">The wipe event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the announcement has been posted (or skipped).</returns>
    Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken cancellationToken);
}
