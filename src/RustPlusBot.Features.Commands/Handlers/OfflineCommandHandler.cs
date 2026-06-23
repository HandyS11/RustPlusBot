using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!offline — lists teammates not currently connected.</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class OfflineCommandHandler(IRustServerQuery query, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "offline";

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

        var names = team.Members.Where(m => !m.IsOnline).Select(m => m.Name).ToList();
        if (names.Count == 0)
        {
            return localizer.Get("command.offline.none", context.Culture);
        }

        return localizer.Get("command.offline.ok", context.Culture, names.Count, string.Join(", ", names));
    }
}
