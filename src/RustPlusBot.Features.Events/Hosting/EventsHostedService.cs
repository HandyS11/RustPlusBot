using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Events.Relaying;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Events.Hosting;

/// <summary>Runs the marker-change relay loop and the disconnect-clear loop.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="relay">Relays marker deltas into Discord #events.</param>
/// <param name="store">Cleared on disconnect.</param>
/// <param name="scopeFactory">Opens scopes to read connection state.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class EventsHostedService(
    IEventBus eventBus,
    EventRelay relay,
    EventStateStore store,
    IServiceScopeFactory scopeFactory,
    ILogger<EventsHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _disconnectLoop;
    private Task? _relayLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _relayLoop = Task.Run(() => ConsumeMarkerEventsAsync(_cts.Token), CancellationToken.None);
        _disconnectLoop = Task.Run(() => ConsumeConnectionStatusEventsAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _relayLoop, _disconnectLoop
                 }.Where(t => t is not null))
        {
            try
            {
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks — these are our own loop tasks, joined on stop.
                await loop!.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private async Task ConsumeMarkerEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<MapMarkersChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await relay.RelayAsync(evt, cancellationToken).ConfigureAwait(false);
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
            LogRelayLoopFaulted(logger, ex);
        }
    }

    private async Task ConsumeConnectionStatusEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await ClearIfDisconnectedAsync(evt, cancellationToken).ConfigureAwait(false);
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
                store.Clear(evt.GuildId, evt.ServerId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Event relay loop faulted.")]
    private static partial void LogRelayLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Disconnect-clear loop faulted.")]
    private static partial void LogDisconnectLoopFaulted(ILogger logger, Exception exception);
}
