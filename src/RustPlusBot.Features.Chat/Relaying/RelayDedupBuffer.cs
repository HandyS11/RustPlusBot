using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Chat.Relaying;

/// <summary>
/// Short-lived record of lines the bridge relayed into the game, keyed by (guild, server). When the bot's
/// active player echoes a relayed line back on the socket, <see cref="TryConsume"/> matches and removes one
/// entry so the relay drops the echo instead of re-posting it to Discord.
/// </summary>
/// <param name="clock">Drives entry expiry.</param>
internal sealed class RelayDedupBuffer(IClock clock)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), List<Entry>> _entries = new();

    /// <summary>Records that <paramref name="text"/> was relayed into the game for <paramref name="key"/>.</summary>
    /// <param name="key">The (guild, server) the line was relayed to.</param>
    /// <param name="text">The exact formatted text that was sent.</param>
    public void Record((ulong Guild, Guid Server) key, string text)
    {
        var list = _entries.GetOrAdd(key, _ => []);
        lock (list)
        {
            Prune(list);
            list.Add(new Entry(text, clock.UtcNow + Ttl));
        }
    }

    /// <summary>Removes and returns true for the first live entry exactly matching <paramref name="text"/>.</summary>
    /// <param name="key">The (guild, server) the echo arrived on.</param>
    /// <param name="text">The echoed text to match.</param>
    /// <returns>True if a matching entry was consumed (the line is our own echo).</returns>
    public bool TryConsume((ulong Guild, Guid Server) key, string text)
    {
        if (!_entries.TryGetValue(key, out var list))
        {
            return false;
        }

        lock (list)
        {
            Prune(list);
            var index = list.FindIndex(e => string.Equals(e.Text, text, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            list.RemoveAt(index);
            return true;
        }
    }

    private void Prune(List<Entry> list)
    {
        var now = clock.UtcNow;
        list.RemoveAll(e => e.ExpiresAt <= now);
    }

    private readonly record struct Entry(string Text, DateTimeOffset ExpiresAt);
}
