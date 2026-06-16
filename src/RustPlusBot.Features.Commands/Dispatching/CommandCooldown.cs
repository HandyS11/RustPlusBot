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
    public bool TryConsume(Guid serverId, string name)
    {
        var now = clock.UtcNow;
        var key = (serverId, name);
        var allowed = true;
        _last.AddOrUpdate(
            key,
            now,
            (_, previous) =>
            {
                if (now - previous < _cooldown)
                {
                    allowed = false;
                    return previous;
                }

                return now;
            });
        return allowed;
    }
}
