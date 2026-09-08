using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Hosting;
using RustPlusBot.Features.Switches.Pairing;
using RustPlusBot.Features.Switches.Relaying;

namespace RustPlusBot.Features.Switches.Hosting;

/// <summary>Runs the switch-pairing loop, the switch-state/connection-status relay loop, and the wipe-purge loop.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="coordinator">Handles paired switches.</param>
/// <param name="relay">Re-renders switches on state/connection changes.</param>
/// <param name="purger">Purges switches when a server wipes.</param>
/// <param name="logger">The logger.</param>
internal sealed class SwitchesHostedService(
    IEventBus eventBus,
    SwitchPairingCoordinator coordinator,
    SwitchStateRelay relay,
    SwitchWipePurger purger,
    ILogger<SwitchesHostedService> logger) : EventLoopHostedService(eventBus, logger)
{
    /// <inheritdoc />
    protected override IEnumerable<EventLoopRegistration> Loops =>
    [
        Loop<SwitchPairedEvent>("switch pairing", coordinator.HandlePairedAsync),
        Loop<SwitchStateChangedEvent>("switch state relay", relay.HandleStateChangedAsync),
        Loop<ConnectionStatusChangedEvent>("switch connection-status relay", relay.HandleConnectionStatusAsync),
        Loop<SmartDeviceTriggeredEvent>("switch device-triggered relay", relay.HandleDeviceTriggeredAsync),
        Loop<DeviceReachabilityChangedEvent>("switch reachability relay", relay.HandleReachabilityChangedAsync),
        Loop<ServerWipedEvent>("switch wipe-purge", purger.HandleServerWipedAsync),
    ];
}
