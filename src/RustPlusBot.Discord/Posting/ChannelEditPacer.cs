using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Discord.Posting;

/// <summary>
///     Per-channel edit pacer. Discord meters message edits per channel; the workspace refreshes several
///     #info messages back-to-back on one tick, and a zero-gap burst exhausts that bucket, so Discord.Net
///     preemptively waits on the tail edits and logs a rate-limit warning each time. This spreads the
///     burst: successive edits to the same channel are held at least <c>minGap</c> apart. An edit to an
///     idle channel (the common steady-state case) is not delayed at all.
/// </summary>
/// <param name="clock">Supplies the current time.</param>
/// <param name="minGap">The minimum spacing between successive edits to one channel.</param>
public sealed class ChannelEditPacer(IClock clock, TimeSpan minGap) : IChannelEditPacer
{
    /// <summary>The earliest instant the next edit to each channel may be sent.</summary>
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _nextAllowed = new();

    /// <summary>Uses the default one-second spacing, comfortably under Discord's per-channel edit limit.</summary>
    /// <param name="clock">Supplies the current time.</param>
    public ChannelEditPacer(IClock clock) : this(clock, TimeSpan.FromSeconds(1))
    {
    }

    /// <inheritdoc />
    public async Task PaceAsync(ulong channelId, CancellationToken cancellationToken)
    {
        var wait = Reserve(channelId);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Atomically reserves this call's edit slot for the channel and returns how long the caller must
    ///     wait before sending it. Pure with respect to the clock, so the pacing decision is testable
    ///     without real delays.
    /// </summary>
    /// <param name="channelId">The Discord channel the edit targets.</param>
    /// <returns>The wait before the reserved slot; <see cref="TimeSpan.Zero" /> when the channel is idle.</returns>
    public TimeSpan Reserve(ulong channelId)
    {
        var now = clock.UtcNow;

        // Reserve the slot after this one, starting from whichever is later: now, or the slot a
        // concurrent/prior edit already claimed. The value returned is that next slot.
        var nextAllowed = _nextAllowed.AddOrUpdate(
            channelId,
            now + minGap,
            (_, reserved) => (reserved > now ? reserved : now) + minGap);

        // This call's own slot is one gap before the slot it just reserved for the following edit.
        var plannedSend = nextAllowed - minGap;
        var wait = plannedSend - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }
}
