using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!events — lists the most recent live events.</summary>
/// <param name="state">The live event state.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class EventsCommandHandler(IEventState state, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "events";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var events = state.GetRecentEvents(context.GuildId, context.ServerId);
        if (events.Count == 0)
        {
            return Task.FromResult<string?>(localizer.Get("command.events.none", context.Culture));
        }

        var parts = events.Select(e =>
        {
            var grid = GridReference.From(e.X, e.Y, e.Dimensions);
            var key = e.Kind switch
            {
                MapEventKind.CargoEntered => "command.event.cargoentered",
                MapEventKind.CargoLeft => "command.event.cargoleft",
                MapEventKind.HeliEntered => "command.event.helientered",
                MapEventKind.HeliLeft => "command.event.helileft",
                MapEventKind.ChinookSpawned => "command.event.chinookspawned",
                _ => "command.event.chinookspawned",
            };
            return localizer.Get(key, context.Culture, grid);
        });

        return Task.FromResult<string?>(
            localizer.Get("command.events.ok", context.Culture, string.Join(", ", parts)));
    }
}
