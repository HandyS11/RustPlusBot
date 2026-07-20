using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!alive — reports per-member survival times (longest first); dead members shown as "dead".</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
/// <param name="clock">The clock used to compute survival durations.</param>
internal sealed class AliveCommandHandler(IRustServerQuery query, ILocalizer localizer, IClock clock)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "alive";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var team = await query.GetTeamInfoAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        if (team.Members.Count == 0)
        {
            return localizer.Get("command.team.none", context.Culture);
        }

        var now = clock.UtcNow;
        var alive = team.Members
            .Where(m => m.IsAlive)
            .OrderByDescending(m => now - m.LastSpawnTimeUtc)
            .Select(m => localizer.Get(
                "command.alive.member", context.Culture,
                m.Name, DurationFormat.Compact(now - m.LastSpawnTimeUtc)));
        var dead = team.Members
            .Where(m => !m.IsAlive)
            .Select(m => localizer.Get("command.alive.dead", context.Culture, m.Name));

        var parts = alive.Concat(dead).ToList();
        return localizer.Get("command.alive.ok", context.Culture, string.Join(", ", parts));
    }
}
