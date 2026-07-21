using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Abstractions.Chat;

/// <summary>
/// Short-lived record of lines the bridge relayed into the game, keyed by (channel kind, guild, server). When
/// the bot's active player echoes a relayed line back on the socket, <see cref="TryConsume"/> matches and
/// removes one entry so the relay drops the echo instead of re-posting it to Discord. The
/// <see cref="ChatChannelKind"/> is part of the key on purpose: the same text relayed to both team and clan
/// chat within the TTL produces one entry per channel, so a team echo cannot cancel the clan entry (or vice
/// versa) and leave the other channel's echo to be re-posted.
/// </summary>
/// <param name="clock">Drives entry expiry.</param>
public sealed class RelayDedupBuffer(IClock clock)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<(ChatChannelKind Kind, ulong Guild, Guid Server), List<Entry>> _entries =
        new();

    /// <summary>Records that <paramref name="text"/> was relayed into the game for <paramref name="key"/>.</summary>
    /// <param name="kind">The chat channel the line was relayed to.</param>
    /// <param name="key">The (guild, server) the line was relayed to.</param>
    /// <param name="text">The exact formatted text that was sent.</param>
    public void Record(ChatChannelKind kind, (ulong Guild, Guid Server) key, string text)
    {
        var list = _entries.GetOrAdd((kind, key.Guild, key.Server), _ => []);
        lock (list)
        {
            Prune(list);
            list.Add(new Entry(text, clock.UtcNow + Ttl));
        }
    }

    /// <summary>Removes and returns true for the first live entry exactly matching <paramref name="text"/>.</summary>
    /// <param name="kind">The chat channel the echo arrived on.</param>
    /// <param name="key">The (guild, server) the echo arrived on.</param>
    /// <param name="text">The echoed text to match.</param>
    /// <returns>True if a matching entry was consumed (the line is our own echo).</returns>
    public bool TryConsume(ChatChannelKind kind, (ulong Guild, Guid Server) key, string text)
    {
        if (!_entries.TryGetValue((kind, key.Guild, key.Server), out var list))
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
