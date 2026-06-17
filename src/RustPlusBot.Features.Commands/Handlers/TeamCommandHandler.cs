using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!team — lists every team member's name.</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class TeamCommandHandler(IRustServerQuery query, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "team";

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

        var names = team.Members.Select(m => m.Name).ToList();
        return localizer.Get("command.team.ok", context.Culture, names.Count, string.Join(", ", names));
    }
}
