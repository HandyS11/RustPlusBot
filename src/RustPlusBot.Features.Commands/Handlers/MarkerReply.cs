using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Events.Formatting;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>Shared map-marker reply formatting for the !cargo/!heli/!chinook handlers.</summary>
internal static class MarkerReply
{
    /// <summary>Formats the localized reply for the most recent active marker of a kind.</summary>
    /// <param name="services">The collaborators needed to format the reply.</param>
    /// <param name="context">The command context.</param>
    /// <param name="kind">Which marker kind to report.</param>
    /// <param name="prefix">The localization key prefix ("command.cargo" / "command.heli" / "command.chinook").</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The localized reply.</returns>
    public static async Task<string> ForAsync(
        MarkerReplyServices services,
        CommandContext context,
        MarkerKind kind,
        string prefix,
        CancellationToken cancellationToken)
    {
        var markers = services.State.GetActiveMarkers(context.GuildId, context.ServerId, kind);
        if (markers.Count == 0)
        {
            return services.Localizer.Get($"{prefix}.none", context.Culture);
        }

        var settings = await services.MapSettings.GetAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        var m = markers[0];
        var location = MapLocation.Describe(services.Localizer, context.Culture, m.X, m.Y, m.Dimensions,
            settings.GridStyle);
        var ago = DurationFormat.Compact(services.Clock.UtcNow - m.SeenAtUtc);
        return services.Localizer.Get($"{prefix}.ok{(location.IsDirection ? ".dir" : string.Empty)}", context.Culture,
            location.Text, ago);
    }
}
