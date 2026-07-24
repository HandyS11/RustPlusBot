using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Map.RustMaps;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Map.Hosting;

/// <summary>
/// Drives credit-safe RustMaps generation for the #info map: registers each connected server's (size, seed)
/// key with the coordinator, advances pending generations on a poll interval, and publishes
/// <see cref="InfoMapReadyEvent"/> at most once per key so the workspace reconciler picks up the ready render.
/// No posting here — delivery is a reconciled Workspace message (<c>ServerInfoMapMessageRenderer</c>) that
/// reads the coordinator through <c>IInfoMapReadModel</c>.
/// </summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="coordinator">The shared RustMaps generation state.</param>
/// <param name="driver">Advances one map key's generation one step per tick.</param>
/// <param name="query">Live query seam (world size/seed).</param>
/// <param name="options">Supplies the generation-poll interval.</param>
/// <param name="scopeFactory">Opens scopes to read connection state.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class InfoMapHostedService(
    IEventBus eventBus,
    IRustMapsMapCoordinator coordinator,
    RustMapsGenerationDriver driver,
    IRustServerQuery query,
    IOptions<MapOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<InfoMapHostedService> logger) : IHostedService, IDisposable
{
    /// <summary>Keys already announced via <see cref="InfoMapReadyEvent"/> — each key is published at most once.</summary>
    private readonly HashSet<RustMapsMapKey> _announced = [];

    private readonly CancellationTokenSource _cts = new();
    private Task? _statusLoop;
    private Task? _tickLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _statusLoop = Task.Run(() => ConsumeConnectionStatusAsync(_cts.Token), CancellationToken.None);
        _tickLoop = Task.Run(() => RunTickAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _statusLoop, _tickLoop
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

    /// <summary>
    /// Advances every pending RustMaps generation key strictly one-at-a-time (sequential, never
    /// concurrent) — the driver's check-then-spend path is not atomic across awaits, so concurrent
    /// calls for the same key could double-spend real RustMaps credits. Then announces any key that
    /// just reached Ready to every one of its requesters.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    private async Task RunTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(options.Value.RustMaps.GenerationPollInterval, cancellationToken)
                    .ConfigureAwait(false);

                // Register from the source of truth every tick, not only from the live-only
                // ConnectionStatusChangedEvent (which the connection may publish before this service has
                // subscribed on startup, so it is missed and the server would otherwise never generate).
                await RegisterConnectedServersAsync(cancellationToken).ConfigureAwait(false);

                var pending = coordinator.PendingKeys();
                foreach (var key in pending)
                {
                    await driver.AdvanceAsync(key, cancellationToken).ConfigureAwait(false);
                }

                await AnnounceReadyKeysAsync(pending, cancellationToken).ConfigureAwait(false);
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
            LogTickFaulted(logger, ex);
        }
    }

    /// <summary>
    /// Registers every currently-connected server's (size, seed) key with the coordinator. Idempotent, and
    /// independent of the connection-status event — <see cref="IRustServerQuery.GetWorldAsync"/> returns null
    /// for a server that is not connected/queryable, so only live servers are registered.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    private async Task RegisterConnectedServersAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<(ulong Guild, Guid Server)> servers;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            servers = await store.ListConnectableServersAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var (guild, server) in servers)
        {
            var world = await query.GetWorldAsync(guild, server, cancellationToken).ConfigureAwait(false);
            if (world is not null)
            {
                coordinator.Register(new RustMapsMapKey((int)world.WorldSize, (int)world.Seed), guild, server);
            }
        }
    }

    private async Task AnnounceReadyKeysAsync(IReadOnlyList<RustMapsMapKey> keys, CancellationToken cancellationToken)
    {
        foreach (var key in keys)
        {
            if (_announced.Contains(key) || coordinator.Snapshot(key).State != RustMapsGenerationState.Ready)
            {
                continue;
            }

            _announced.Add(key);
            foreach (var (guild, server) in coordinator.Requesters(key))
            {
                await eventBus.PublishAsync(new InfoMapReadyEvent(guild, server), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ConsumeConnectionStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.ConsumeAsync<ConnectionStatusChangedEvent>(OnConnectionStatusAsync,
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
            LogStatusFaulted(logger, ex);
        }
    }

    private async Task OnConnectionStatusAsync(ConnectionStatusChangedEvent evt, CancellationToken cancellationToken)
    {
        // Re-derive current status from the store rather than trusting the event's IsConnected payload: the
        // in-process bus is an unbounded queue, so by the time this loop dequeues the event the live status
        // may already have moved on (mirrors MapHostedService.OnConnectionStatusAsync).
        bool connected;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var state = await store.GetStateAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
            connected = state is { Status: ConnectionStatus.Connected };
        }

        if (!connected)
        {
            return;
        }

        var world = await query.GetWorldAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
        if (world is null)
        {
            return;
        }

        coordinator.Register(new RustMapsMapKey((int)world.WorldSize, (int)world.Seed), evt.GuildId, evt.ServerId);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling {EventType} failed; skipping that event.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Info-map tick loop faulted.")]
    private static partial void LogTickFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Info-map connection-status loop faulted.")]
    private static partial void LogStatusFaulted(ILogger logger, Exception exception);
}
