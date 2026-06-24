using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!prox [name] — distance from the caller to each other teammate (optionally one).</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class ProxCommandHandler(IRustServerQuery query, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "prox";

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

        var self = team.Members.FirstOrDefault(m => m.SteamId == context.SenderSteamId);
        if (self is null)
        {
            return localizer.Get("command.prox.selfunknown", context.Culture);
        }

        var others = team.Members.Where(m => m.SteamId != context.SenderSteamId).ToList();
        if (others.Count == 0)
        {
            return localizer.Get("command.prox.alone", context.Culture);
        }

        var nameArg = context.Args.Count > 0 ? context.Args[0] : null;
        var matches = TeamMemberFilter.ByName(others, nameArg);
        if (matches.Count == 0)
        {
            return localizer.Get("command.team.nomatch", context.Culture, nameArg ?? string.Empty);
        }

        var parts = matches.Select(m => localizer.Get(
            "command.prox.member", context.Culture, m.Name, Distance.Between(self.X, self.Y, m.X, m.Y)));
        return localizer.Get("command.prox.ok", context.Culture, string.Join(", ", parts));
    }
}
