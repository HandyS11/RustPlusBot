using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Vending.Relaying;

namespace RustPlusBot.Features.Vending.Hosting;

/// <summary>
/// Runs the vending feature's three event-consumer loops: the marker-poll relay loop (index refresh plus
/// #vending reconciliation), the connection-status loop (drops a disconnected server's index), and the
/// wipe-purge loop.
/// </summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="relay">Re-indexes and reconciles #vending on poll/connection-status changes.</param>
/// <param name="purger">Purges vending state when a server wipes.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class VendingHostedService(
    IEventBus eventBus,
    VendingNotificationRelay relay,
    VendingWipePurger purger,
    ILogger<VendingHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _observedLoop;
    private Task? _statusLoop;
    private Task? _wipedLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe synchronously, before the background loops are scheduled: InMemoryEventBus registers
        // a subscription as soon as SubscribeAsync is called, so subscribing inside Task.Run would drop
        // every event published between start-up and the task actually running.
        var observed = eventBus.SubscribeAsync<VendingMachinesObservedEvent>(_cts.Token);
        var status = eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(_cts.Token);
        var wiped = eventBus.SubscribeAsync<ServerWipedEvent>(_cts.Token);

        _observedLoop = Task.Run(
            () => observed.ConsumeAsync(
                relay.HandleObservedAsync,
                ex => LogHandlerFailed(logger, ex, nameof(VendingMachinesObservedEvent)),
                _cts.Token),
            CancellationToken.None);
        _statusLoop = Task.Run(
            () => status.ConsumeAsync(
                relay.HandleConnectionStatusAsync,
                ex => LogHandlerFailed(logger, ex, nameof(ConnectionStatusChangedEvent)),
                _cts.Token),
            CancellationToken.None);
        _wipedLoop = Task.Run(
            () => wiped.ConsumeAsync(
                purger.HandleServerWipedAsync,
                ex => LogHandlerFailed(logger, ex, nameof(ServerWipedEvent)),
                _cts.Token),
            CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[] { _observedLoop, _statusLoop, _wipedLoop }.OfType<Task>())
        {
            try
            {
#pragma warning disable VSTHRD003 // Our own loop tasks, joined on stop.
                await loop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling {EventType} failed; skipping that event.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string eventType);
}
