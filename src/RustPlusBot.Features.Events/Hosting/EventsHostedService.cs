using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Events.Relaying;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Events.Hosting;

/// <summary>Bundles the state-store collaborators injected into <see cref="EventsHostedService"/>.</summary>
/// <param name="Store">The marker state store, cleared on disconnect.</param>
/// <param name="RigStore">The rig state store, advanced by the tick and cleared on disconnect.</param>
internal sealed record EventStores(EventStateStore Store, RigStateStore RigStore);

/// <summary>Runs the marker relay loop, the rig-event relay loop, the rig-timer tick, and the disconnect-clear loop.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="relay">Relays marker + rig events into Discord #events and in-game chat.</param>
/// <param name="stores">Bundles the marker and rig state stores.</param>
/// <param name="clock">Supplies the current time for the tick.</param>
/// <param name="options">Supplies the rig-tick interval.</param>
/// <param name="scopeFactory">Opens scopes to read connection state.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class EventsHostedService(
    IEventBus eventBus,
    EventRelay relay,
    EventStores stores,
    IClock clock,
    IOptions<ConnectionOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<EventsHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _disconnectLoop;
    private Task? _relayLoop;
    private Task? _rigLoop;
    private Task? _tickLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _relayLoop = Task.Run(() => ConsumeMarkerEventsAsync(_cts.Token), CancellationToken.None);
        _rigLoop = Task.Run(() => ConsumeRigEventsAsync(_cts.Token), CancellationToken.None);
        _tickLoop = Task.Run(() => RunRigTickAsync(_cts.Token), CancellationToken.None);
        _disconnectLoop = Task.Run(() => ConsumeConnectionStatusEventsAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _relayLoop, _rigLoop, _tickLoop, _disconnectLoop
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

    /// <summary>Runs one rig-timer tick: advances rig phases and publishes any timed boundary crossings.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the tick has published all crossings.</returns>
    internal async Task TickOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var c in stores.RigStore.Advance(clock.UtcNow))
        {
            await eventBus.PublishAsync(
                    new RigStateChangedEvent(c.GuildId, c.ServerId, c.Rig, c.Kind, c.X, c.Y, c.Dimensions),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RunRigTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await TickOnceAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(options.Value.RigTickInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting tick must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRigTickFaulted(logger, ex);
        }
    }

    private async Task ConsumeMarkerEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.ConsumeAsync<MapMarkersChangedEvent>(relay.RelayAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(MapMarkersChangedEvent)), cancellationToken)
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
            LogRelayLoopFaulted(logger, ex);
        }
    }

    private async Task ConsumeRigEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.ConsumeAsync<RigStateChangedEvent>(relay.RelayRigAsync,
                    ex => LogHandlerFailed(logger, ex, nameof(RigStateChangedEvent)), cancellationToken)
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
            LogRigLoopFaulted(logger, ex);
        }
    }

    private async Task ConsumeConnectionStatusEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.ConsumeAsync<ConnectionStatusChangedEvent>(ClearIfDisconnectedAsync,
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
            LogDisconnectLoopFaulted(logger, ex);
        }
    }

    private async Task ClearIfDisconnectedAsync(ConnectionStatusChangedEvent evt, CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var connectionStore = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var state = await connectionStore.GetStateAsync(evt.GuildId, evt.ServerId, cancellationToken)
                .ConfigureAwait(false);
            if (state is null || state.Status != ConnectionStatus.Connected)
            {
                stores.Store.Clear(evt.GuildId, evt.ServerId);
                stores.RigStore.Clear(evt.GuildId, evt.ServerId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling {EventType} failed; skipping that event.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Event relay loop faulted.")]
    private static partial void LogRelayLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rig relay loop faulted.")]
    private static partial void LogRigLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rig tick loop faulted.")]
    private static partial void LogRigTickFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Disconnect-clear loop faulted.")]
    private static partial void LogDisconnectLoopFaulted(ILogger logger, Exception exception);
}
