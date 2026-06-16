using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Commands.Hosting;

/// <summary>Process-uptime baseline, captured when the singleton is first constructed.</summary>
/// <param name="clock">The clock.</param>
internal sealed class BotUptime(IClock clock)
{
    private readonly DateTimeOffset _start = clock.UtcNow;

    /// <summary>How long the bot has been running.</summary>
    public TimeSpan Elapsed => clock.UtcNow - _start;
}
