using System.Globalization;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Commands.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!steamid [name] — reports teammates' Steam ids (all, or filtered by partial name).</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class SteamIdCommandHandler(IRustServerQuery query, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "steamid";

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

        var nameArg = context.Args.Count > 0 ? context.Args[0] : null;
        var matches = TeamMemberFilter.ByName(team.Members, nameArg);
        if (matches.Count == 0)
        {
            return localizer.Get("command.team.nomatch", context.Culture, nameArg ?? string.Empty);
        }

        var pairs = matches.Select(m =>
            string.Create(CultureInfo.InvariantCulture, $"{m.Name} {m.SteamId}"));
        return localizer.Get("command.steamid.ok", context.Culture, string.Join(", ", pairs));
    }
}
