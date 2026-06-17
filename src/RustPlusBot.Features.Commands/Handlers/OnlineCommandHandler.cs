using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!online — lists currently-connected teammates.</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class OnlineCommandHandler(IRustServerQuery query, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "online";

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

        var names = team.Members.Where(m => m.IsOnline).Select(m => m.Name).ToList();
        if (names.Count == 0)
        {
            return localizer.Get("command.online.none", context.Culture);
        }

        return localizer.Get("command.online.ok", context.Culture, names.Count, string.Join(", ", names));
    }
}
