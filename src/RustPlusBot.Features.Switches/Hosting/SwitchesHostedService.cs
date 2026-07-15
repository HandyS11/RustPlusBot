using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Switches.Pairing;
using RustPlusBot.Features.Switches.Relaying;

namespace RustPlusBot.Features.Switches.Hosting;

/// <summary>Runs the switch-pairing loop, the switch-state/connection-status relay loop, and the wipe-purge loop.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="coordinator">Handles paired switches.</param>
/// <param name="relay">Re-renders switches on state/connection changes.</param>
/// <param name="purger">Purges switches when a server wipes.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class SwitchesHostedService(
    IEventBus eventBus,
    SwitchPairingCoordinator coordinator,
    SwitchStateRelay relay,
    SwitchWipePurger purger,
    ILogger<SwitchesHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _deviceLoop;
    private Task? _pairedLoop;
    private Task? _reachabilityLoop;
    private Task? _stateLoop;
    private Task? _statusLoop;
    private Task? _wipedLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _pairedLoop = Task.Run(() => ConsumePairedAsync(_cts.Token), CancellationToken.None);
        _stateLoop = Task.Run(() => ConsumeStateAsync(_cts.Token), CancellationToken.None);
        _statusLoop = Task.Run(() => ConsumeStatusAsync(_cts.Token), CancellationToken.None);
        _deviceLoop = Task.Run(() => ConsumeDeviceTriggeredAsync(_cts.Token), CancellationToken.None);
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
                     _pairedLoop, _stateLoop, _statusLoop, _deviceLoop, _reachabilityLoop, _wipedLoop
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
            await foreach (var evt in eventBus.SubscribeAsync<SwitchPairedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await coordinator.HandlePairedAsync(evt, cancellationToken).ConfigureAwait(false);
            }
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

    private async Task ConsumeStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<SwitchStateChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await relay.HandleStateChangedAsync(evt, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogStateLoopFaulted(logger, ex);
        }
    }

    private async Task ConsumeStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await relay.HandleConnectionStatusAsync(evt, cancellationToken).ConfigureAwait(false);
            }
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

    private async Task ConsumeDeviceTriggeredAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<SmartDeviceTriggeredEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await relay.HandleDeviceTriggeredAsync(evt, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDeviceLoopFaulted(logger, ex);
        }
    }

    private async Task ConsumeReachabilityChangedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<DeviceReachabilityChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await relay.HandleReachabilityChangedAsync(evt, cancellationToken).ConfigureAwait(false);
            }
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
            await foreach (var evt in eventBus.SubscribeAsync<ServerWipedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await purger.HandleServerWipedAsync(evt, cancellationToken).ConfigureAwait(false);
            }
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Switch device-triggered relay loop faulted.")]
    private static partial void LogDeviceLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Switch pairing loop faulted.")]
    private static partial void LogPairedLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Switch state relay loop faulted.")]
    private static partial void LogStateLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Switch connection-status relay loop faulted.")]
    private static partial void LogStatusLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Switch reachability relay loop faulted.")]
    private static partial void LogReachabilityLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Switch wipe-purge loop faulted.")]
    private static partial void LogWipedLoopFaulted(ILogger logger, Exception exception);
}
