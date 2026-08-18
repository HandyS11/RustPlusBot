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

        _observedLoop = Task.Run(() => ConsumeObservedAsync(observed, _cts.Token), CancellationToken.None);
        _statusLoop = Task.Run(() => ConsumeStatusAsync(status, _cts.Token), CancellationToken.None);
        _wipedLoop = Task.Run(() => ConsumeWipedAsync(wiped, _cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _observedLoop, _statusLoop, _wipedLoop
                 }.OfType<Task>())
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

    /// <summary>Drains the marker-poll subscription, logging and continuing on handler failures.</summary>
    /// <param name="events">The eagerly-established subscription stream.</param>
    /// <param name="cancellationToken">Ends the loop when cancelled.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    private async Task ConsumeObservedAsync(
        IAsyncEnumerable<VendingMachinesObservedEvent> events,
        CancellationToken cancellationToken)
    {
        try
        {
            await events.ConsumeAsync(
                    relay.HandleObservedAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(VendingMachinesObservedEvent)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogObservedLoopFaulted(logger, ex);
        }
    }

    /// <summary>Drains the connection-status subscription, logging and continuing on handler failures.</summary>
    /// <param name="events">The eagerly-established subscription stream.</param>
    /// <param name="cancellationToken">Ends the loop when cancelled.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    private async Task ConsumeStatusAsync(
        IAsyncEnumerable<ConnectionStatusChangedEvent> events,
        CancellationToken cancellationToken)
    {
        try
        {
            await events.ConsumeAsync(
                    relay.HandleConnectionStatusAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(ConnectionStatusChangedEvent)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogStatusLoopFaulted(logger, ex);
        }
    }

    /// <summary>Drains the wipe subscription, logging and continuing on handler failures.</summary>
    /// <param name="events">The eagerly-established subscription stream.</param>
    /// <param name="cancellationToken">Ends the loop when cancelled.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    private async Task ConsumeWipedAsync(
        IAsyncEnumerable<ServerWipedEvent> events,
        CancellationToken cancellationToken)
    {
        try
        {
            await events.ConsumeAsync(
                    purger.HandleServerWipedAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(ServerWipedEvent)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogWipedLoopFaulted(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling {EventType} failed; skipping that event.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vending marker-poll relay loop faulted.")]
    private static partial void LogObservedLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vending connection-status relay loop faulted.")]
    private static partial void LogStatusLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vending wipe-purge loop faulted.")]
    private static partial void LogWipedLoopFaulted(ILogger logger, Exception exception);
}
