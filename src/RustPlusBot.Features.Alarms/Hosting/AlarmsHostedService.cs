using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Alarms.Pairing;
using RustPlusBot.Features.Alarms.Relaying;

namespace RustPlusBot.Features.Alarms.Hosting;

/// <summary>Runs the alarm-pairing loop, the alarm-triggered relay loop, the connection-status relay loop, the per-device reachability loop, the observed-state sync loop, and the wipe-purge loop.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="coordinator">Handles paired alarms.</param>
/// <param name="relay">Re-renders alarms on trigger/connection/reachability/observed-state changes.</param>
/// <param name="purger">Purges alarms when a server wipes.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class AlarmsHostedService(
    IEventBus eventBus,
    AlarmPairingCoordinator coordinator,
    AlarmStateRelay relay,
    AlarmWipePurger purger,
    ILogger<AlarmsHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _observedLoop;
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
        _observedLoop = Task.Run(() => ConsumeStateObservedAsync(_cts.Token), CancellationToken.None);
        _wipedLoop = Task.Run(() => ConsumeWipedAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _pairedLoop, _triggeredLoop, _statusLoop, _reachabilityLoop, _observedLoop, _wipedLoop
                 }.Where(t => t is not null))
        {
            try
            {
#pragma warning disable VSTHRD003 // Our own loop tasks, joined on stop.
                await loop!.ConfigureAwait(false);
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
            await eventBus.ConsumeAsync<AlarmPairedEvent>(coordinator.HandlePairedAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(AlarmPairedEvent)), cancellationToken)
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
            await eventBus.ConsumeAsync<SmartDeviceTriggeredEvent>(relay.HandleTriggeredAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(SmartDeviceTriggeredEvent)), cancellationToken)
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

    private async Task ConsumeStateObservedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.ConsumeAsync<SmartDeviceStateObservedEvent>(relay.HandleStateObservedAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(SmartDeviceStateObservedEvent)), cancellationToken)
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Alarm pairing loop faulted.")]
    private static partial void LogPairedLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Alarm device-triggered relay loop faulted.")]
    private static partial void LogTriggeredLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Alarm connection-status relay loop faulted.")]
    private static partial void LogStatusLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Alarm reachability relay loop faulted.")]
    private static partial void LogReachabilityLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Alarm observed-state sync loop faulted.")]
    private static partial void LogObservedLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Alarm wipe-purge loop faulted.")]
    private static partial void LogWipedLoopFaulted(ILogger logger, Exception exception);
}
