using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!events — lists the most recent live events.</summary>
/// <param name="state">The live event state.</param>
/// <param name="localizer">The reply localizer.</param>
/// <param name="mapSettings">Supplies the server's grid style.</param>
internal sealed class EventsCommandHandler(
    IEventState state,
    ILocalizer localizer,
    IMapSettingsStore mapSettings) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "events";

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">A recent event has an unsupported <see cref="MapEventKind"/>.</exception>
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var events = state.GetRecentEvents(context.GuildId, context.ServerId);
        if (events.Count == 0)
        {
            return localizer.Get("command.events.none", context.Culture);
        }

        var settings = await mapSettings.GetAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        var parts = events.Select(e =>
        {
            var grid = GridReference.From(e.X, e.Y, e.Dimensions, settings.GridStyle);
            var key = e.Kind switch
            {
                MapEventKind.CargoEntered => "command.event.cargoentered",
                MapEventKind.CargoLeft => "command.event.cargoleft",
                MapEventKind.HeliEntered => "command.event.helientered",
                MapEventKind.HeliLeft => "command.event.helileft",
                MapEventKind.ChinookSpawned => "command.event.chinookspawned",
                _ => throw new ArgumentOutOfRangeException(nameof(e), e.Kind, "Unsupported map event kind."),
            };
            return localizer.Get(key, context.Culture, grid);
        });

        return localizer.Get("command.events.ok", context.Culture, string.Join(", ", parts));
    }
}
