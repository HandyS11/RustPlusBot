using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Commands.Dispatching;

/// <summary>Per-(server, command) cooldown. Singleton; in-memory; clock-driven.</summary>
/// <param name="clock">The clock.</param>
/// <param name="options">The command options carrying the cooldown window.</param>
internal sealed class CommandCooldown(IClock clock, IOptions<CommandOptions> options)
{
    private readonly TimeSpan _cooldown = options.Value.Cooldown;
    private readonly ConcurrentDictionary<(Guid Server, string Name), DateTimeOffset> _last = new();

    /// <summary>Returns true if the command may run now (and records the run); false if on cooldown.</summary>
    /// <param name="serverId">The server id.</param>
    /// <param name="name">The lowercase command name.</param>
    /// <remarks>
    /// Uses a lock-free compare-and-swap so concurrent callers for the same key are gated atomically:
    /// exactly one thread wins the write and returns true. (The dispatcher is single-threaded per event,
    /// but this keeps the gate correct under any caller.)
    /// </remarks>
    public bool TryConsume(Guid serverId, string name)
    {
        var now = clock.UtcNow;
        var key = (serverId, name);

        while (true)
        {
            if (!_last.TryGetValue(key, out var previous))
            {
                // No prior run: the thread that inserts first wins; others fall through to the update path.
                if (_last.TryAdd(key, now))
                {
                    return true;
                }

                continue;
            }

            if (now - previous < _cooldown)
            {
                return false;
            }

            // Window elapsed: only the thread whose CAS replaces this exact 'previous' wins.
            if (_last.TryUpdate(key, now, previous))
            {
                return true;
            }
        }
    }
}
