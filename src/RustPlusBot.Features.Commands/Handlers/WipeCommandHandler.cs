using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!wipe — reports how long ago the server last wiped.</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
/// <param name="clock">The clock used to compute the elapsed time since wipe.</param>
internal sealed class WipeCommandHandler(IRustServerQuery query, ILocalizer localizer, IClock clock)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "wipe";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var info = await query.GetServerInfoAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (info is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        if (info.WipeTimeUtc is null)
        {
            return localizer.Get("command.wipe.unknown", context.Culture);
        }

        return localizer.Get("command.wipe.ok", context.Culture,
            DurationFormat.Compact(clock.UtcNow - info.WipeTimeUtc.Value));
    }
}
