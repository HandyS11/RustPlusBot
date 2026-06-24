using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!cargo — reports the cargo ship's current grid position, if any.</summary>
/// <param name="state">The live event state.</param>
/// <param name="localizer">The reply localizer.</param>
/// <param name="clock">For "how long ago".</param>
internal sealed class CargoCommandHandler(IEventState state, ILocalizer localizer, IClock clock)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "cargo";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var markers = state.GetActiveMarkers(context.GuildId, context.ServerId, MarkerKind.CargoShip);
        if (markers.Count == 0)
        {
            return Task.FromResult<string?>(localizer.Get("command.cargo.none", context.Culture));
        }

        var m = markers[0];
        var grid = GridReference.From(m.X, m.Y, m.Dimensions);
        var ago = DurationFormat.Compact(clock.UtcNow - m.SeenAtUtc);
        return Task.FromResult<string?>(localizer.Get("command.cargo.ok", context.Culture, grid, ago));
    }
}
