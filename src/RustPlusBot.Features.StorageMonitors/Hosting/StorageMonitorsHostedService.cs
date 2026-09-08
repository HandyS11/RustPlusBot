using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Hosting;
using RustPlusBot.Features.StorageMonitors.Pairing;
using RustPlusBot.Features.StorageMonitors.Relaying;

namespace RustPlusBot.Features.StorageMonitors.Hosting;

/// <summary>Runs the storage-monitor pairing loop, the triggered relay loop, the connection-status relay loop, the reachability relay loop, and the wipe-purge loop.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="coordinator">Handles paired storage monitors.</param>
/// <param name="relay">Re-renders storage monitors on trigger/connection changes.</param>
/// <param name="purger">Purges storage monitors when a server wipes.</param>
/// <param name="logger">The logger.</param>
internal sealed class StorageMonitorsHostedService(
    IEventBus eventBus,
    StorageMonitorPairingCoordinator coordinator,
    StorageMonitorStateRelay relay,
    StorageMonitorWipePurger purger,
    ILogger<StorageMonitorsHostedService> logger) : EventLoopHostedService(eventBus, logger)
{
    /// <inheritdoc />
    protected override IEnumerable<EventLoopRegistration> Loops =>
    [
        Loop<StorageMonitorPairedEvent>("storage-monitor pairing", coordinator.HandlePairedAsync),
        Loop<StorageMonitorTriggeredEvent>("storage-monitor triggered relay", relay.HandleTriggeredAsync),
        Loop<ConnectionStatusChangedEvent>("storage-monitor connection-status relay",
            relay.HandleConnectionStatusAsync),
        Loop<DeviceReachabilityChangedEvent>("storage-monitor reachability relay",
            relay.HandleReachabilityChangedAsync),
        Loop<ServerWipedEvent>("storage-monitor wipe-purge", purger.HandleServerWipedAsync),
    ];
}
