using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!afk — lists team members who have been still (online + alive) past the AFK threshold.</summary>
/// <param name="afk">The live AFK state.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class AfkCommandHandler(IAfkState afk, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "afk";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var members = await afk.GetAfkMembersAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (members is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        if (members.Count == 0)
        {
            return localizer.Get("command.afk.none", context.Culture);
        }

        var parts = members
            .OrderByDescending(m => m.StillFor)
            .Select(m => localizer.Get(
                "command.afk.member", context.Culture, m.Name, DurationFormat.Compact(m.StillFor)));
        return localizer.Get("command.afk.ok", context.Culture, string.Join(", ", parts));
    }
}
