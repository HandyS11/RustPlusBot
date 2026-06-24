using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>Shared map-marker reply formatting for the !cargo/!heli/!chinook handlers.</summary>
internal static class MarkerReply
{
    /// <summary>Formats the localized reply for the most recent active marker of a kind.</summary>
    /// <param name="state">The live event state reader.</param>
    /// <param name="context">The command context.</param>
    /// <param name="kind">Which marker kind to report.</param>
    /// <param name="prefix">The localization key prefix ("command.cargo" / "command.heli" / "command.chinook").</param>
    /// <param name="localizer">The reply localizer.</param>
    /// <param name="clock">For the "how long ago" suffix.</param>
    /// <returns>The localized reply.</returns>
    public static string For(
        IEventState state,
        CommandContext context,
        MarkerKind kind,
        string prefix,
        ILocalizer localizer,
        IClock clock)
    {
        var markers = state.GetActiveMarkers(context.GuildId, context.ServerId, kind);
        if (markers.Count == 0)
        {
            return localizer.Get($"{prefix}.none", context.Culture);
        }

        var m = markers[0];
        var grid = GridReference.From(m.X, m.Y, m.Dimensions);
        var ago = DurationFormat.Compact(clock.UtcNow - m.SeenAtUtc);
        return localizer.Get($"{prefix}.ok", context.Culture, grid, ago);
    }
}
