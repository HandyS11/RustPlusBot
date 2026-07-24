namespace RustPlusBot.Discord.Posting;

/// <summary>
///     Spaces out our own message edits to a channel so a burst never exhausts Discord's per-channel
///     edit bucket (which otherwise makes Discord.Net preemptively throttle the tail of the burst and
///     log a rate-limit warning). Shared process-wide, so every writer to a channel is paced against
///     the same clock.
/// </summary>
public interface IChannelEditPacer
{
    /// <summary>
    ///     Reserves the next edit slot for the channel and, if that slot is in the future, waits until it
    ///     is due. Call immediately before editing; the first edit to an idle channel returns at once.
    /// </summary>
    /// <param name="channelId">The Discord channel the edit targets.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the caller may send its edit.</returns>
    Task PaceAsync(ulong channelId, CancellationToken cancellationToken);
}
