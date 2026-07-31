using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.StorageMonitors.Pairing;
using RustPlusBot.Features.StorageMonitors.Relaying;

namespace RustPlusBot.Features.StorageMonitors.Hosting;

/// <summary>Runs the storage-monitor pairing loop, the triggered relay loop, the connection-status relay loop, the reachability relay loop, and the wipe-purge loop.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="coordinator">Handles paired storage monitors.</param>
/// <param name="relay">Re-renders storage monitors on trigger/connection changes.</param>
/// <param name="purger">Purges storage monitors when a server wipes.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class StorageMonitorsHostedService(
    IEventBus eventBus,
    StorageMonitorPairingCoordinator coordinator,
    StorageMonitorStateRelay relay,
    StorageMonitorWipePurger purger,
    ILogger<StorageMonitorsHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _pairedLoop;
    private Task? _reachabilityLoop;
    private Task? _statusLoop;
    private Task? _triggeredLoop;
    private Task? _wipedLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _pairedLoop = Task.Run(() => ConsumePairedAsync(_cts.Token), CancellationToken.None);
        _triggeredLoop = Task.Run(() => ConsumeTriggeredAsync(_cts.Token), CancellationToken.None);
        _statusLoop = Task.Run(() => ConsumeStatusAsync(_cts.Token), CancellationToken.None);
        _reachabilityLoop = Task.Run(() => ConsumeReachabilityChangedAsync(_cts.Token), CancellationToken.None);
        _wipedLoop = Task.Run(() => ConsumeWipedAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _pairedLoop, _triggeredLoop, _statusLoop, _reachabilityLoop, _wipedLoop
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

    private async Task ConsumePairedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.ConsumeAsync<StorageMonitorPairedEvent>(coordinator.HandlePairedAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(StorageMonitorPairedEvent)), cancellationToken)
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
            LogPairedLoopFaulted(logger, ex);
        }
    }

    private async Task ConsumeTriggeredAsync(CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.ConsumeAsync<StorageMonitorTriggeredEvent>(relay.HandleTriggeredAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(StorageMonitorTriggeredEvent)), cancellationToken)
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
            LogTriggeredLoopFaulted(logger, ex);
        }
    }

    private async Task ConsumeStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.ConsumeAsync<ConnectionStatusChangedEvent>(relay.HandleConnectionStatusAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(ConnectionStatusChangedEvent)), cancellationToken)
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

    private async Task ConsumeReachabilityChangedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.ConsumeAsync<DeviceReachabilityChangedEvent>(relay.HandleReachabilityChangedAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(DeviceReachabilityChangedEvent)), cancellationToken)
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
            LogReachabilityLoopFaulted(logger, ex);
        }
    }

    private async Task ConsumeWipedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.ConsumeAsync<ServerWipedEvent>(purger.HandleServerWipedAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(ServerWipedEvent)), cancellationToken)
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Storage monitor pairing loop faulted.")]
    private static partial void LogPairedLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Storage monitor triggered relay loop faulted.")]
    private static partial void LogTriggeredLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Storage monitor connection-status relay loop faulted.")]
    private static partial void LogStatusLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Storage monitor reachability relay loop faulted.")]
    private static partial void LogReachabilityLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Storage-monitor wipe-purge loop faulted.")]
    private static partial void LogWipedLoopFaulted(ILogger logger, Exception exception);
}
