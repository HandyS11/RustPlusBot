using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Hosting;
using RustPlusBot.Features.Alarms.Pairing;
using RustPlusBot.Features.Alarms.Relaying;

namespace RustPlusBot.Features.Alarms.Hosting;

/// <summary>Runs the alarm-pairing loop, the alarm-triggered relay loop, the connection-status relay loop, the per-device reachability loop, the observed-state sync loop, and the wipe-purge loop.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="coordinator">Handles paired alarms.</param>
/// <param name="relay">Re-renders alarms on trigger/connection/reachability/observed-state changes.</param>
/// <param name="purger">Purges alarms when a server wipes.</param>
/// <param name="logger">The logger.</param>
internal sealed class AlarmsHostedService(
    IEventBus eventBus,
    AlarmPairingCoordinator coordinator,
    AlarmStateRelay relay,
    AlarmWipePurger purger,
    ILogger<AlarmsHostedService> logger) : EventLoopHostedService(eventBus, logger)
{
    /// <inheritdoc />
    protected override IEnumerable<EventLoopRegistration> Loops =>
    [
        Loop<AlarmPairedEvent>("alarm pairing", coordinator.HandlePairedAsync),
        Loop<SmartDeviceTriggeredEvent>("alarm device-triggered relay", relay.HandleTriggeredAsync),
        Loop<ConnectionStatusChangedEvent>("alarm connection-status relay", relay.HandleConnectionStatusAsync),
        Loop<DeviceReachabilityChangedEvent>("alarm reachability relay", relay.HandleReachabilityChangedAsync),
        Loop<SmartDeviceStateObservedEvent>("alarm observed-state sync", relay.HandleStateObservedAsync),
        Loop<ServerWipedEvent>("alarm wipe-purge", purger.HandleServerWipedAsync),
    ];
}
