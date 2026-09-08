using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Hosting;
using RustPlusBot.Features.Players.Relaying;

namespace RustPlusBot.Features.Players.Hosting;

/// <summary>Consumes <see cref="PlayerStateChangedEvent"/> and relays each to #events + in-game chat.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="relay">Relays player transitions.</param>
/// <param name="logger">The logger.</param>
internal sealed class PlayersHostedService(
    IEventBus eventBus,
    PlayerEventRelay relay,
    ILogger<PlayersHostedService> logger) : EventLoopHostedService(eventBus, logger)
{
    /// <inheritdoc />
    protected override IEnumerable<EventLoopRegistration> Loops =>
    [
        Loop<PlayerStateChangedEvent>("player relay", relay.RelayAsync),
    ];
}
