using System.Collections.Concurrent;

namespace RustPlusBot.Discord.Posting;

/// <summary>
///     Per-process memory of the last render successfully sent per Discord message, so identical
///     re-renders (boot primes, periodic contents republishes) skip the PATCH entirely. Entries are
///     committed only after a confirmed send; a failed or deleted message is invalidated so the next
///     render always retries. Restart cost: one edit per message to re-warm the cache — by design.
/// </summary>
public sealed class RenderGate
{
    private readonly ConcurrentDictionary<ulong, string> _lastSent = new();

    /// <summary>Decides whether a render differs from the last committed one for the message.</summary>
    /// <param name="messageId">The Discord message id the render targets.</param>
    /// <param name="canonicalRender">The canonical render string (see <see cref="RenderCanonicalizer"/>).</param>
    /// <returns>True when the render must be sent (differs or nothing committed yet).</returns>
    public bool ShouldSend(ulong messageId, string canonicalRender)
        => !(_lastSent.TryGetValue(messageId, out var last) && last == canonicalRender);

    /// <summary>Records a successfully sent render for the message.</summary>
    /// <param name="messageId">The Discord message id that was posted or edited.</param>
    /// <param name="canonicalRender">The canonical render string that was sent.</param>
    public void Commit(ulong messageId, string canonicalRender) => _lastSent[messageId] = canonicalRender;

    /// <summary>Forgets the message (send failed or message deleted); the next render always sends.</summary>
    /// <param name="messageId">The Discord message id to forget.</param>
    public void Invalidate(ulong messageId) => _lastSent.TryRemove(messageId, out _);
}
