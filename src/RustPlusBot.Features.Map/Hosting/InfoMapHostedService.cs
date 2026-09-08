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
/// key with the coordinator, advances pending generations on a poll interval, checks each ready render
/// against the monuments its servers actually report, and publishes <see cref="InfoMapReadyEvent"/> at most
/// once per (key, server) so the workspace reconciler picks up the verdict.
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
    /// <summary>
    /// (key, server) pairs already announced via <see cref="InfoMapReadyEvent"/> — each is published at most
    /// once, when its verdict is first decided.
    /// </summary>
    private readonly HashSet<(RustMapsMapKey Key, ulong Guild, Guid Server)> _announced = [];

    private readonly CancellationTokenSource _cts = new();
    private Task? _statusLoop;
    private Task? _tickLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe here rather than inside the loop task: Task.Run only queues the body, so a service that
        // subscribed in there would stay unsubscribed until the pool schedules it — and every
        // ConnectionStatusChangedEvent published in that window is dropped. On a saturated host that window
        // is seconds long, which is exactly when connections are being established.
        var statusEvents = eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(_cts.Token);
        _statusLoop = Task.Run(() => ConsumeConnectionStatusAsync(statusEvents, _cts.Token), CancellationToken.None);
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

    /// <summary>
    /// Advances every pending RustMaps generation key strictly one-at-a-time (sequential, never
    /// concurrent) — the driver's check-then-spend path is not atomic across awaits, so concurrent
    /// calls for the same key could double-spend real RustMaps credits. Then checks each ready render
    /// against its requesting servers and announces every server whose verdict has just been decided.
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

                foreach (var key in coordinator.PendingKeys())
                {
                    await driver.AdvanceAsync(key, cancellationToken).ConfigureAwait(false);
                }

                // Verification runs after the advance so a key that just turned Ready is judged on this same
                // tick, and re-runs every tick for servers still undecided (a monument fetch can miss).
                await VerifyReadyMapsAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Checks every ready render against the monuments its requesting servers actually report, and announces
    /// each server the first time its verdict is decided.
    /// </summary>
    /// <remarks>
    /// A (size, seed) does not identify a Rust world: a server running a pre-generated or edited level still
    /// reports the seed from its config, and RustMaps then renders a different island. The monuments come
    /// from the connection's already-resolved map window, so this costs no extra Rust+ map download.
    /// </remarks>
    /// <param name="cancellationToken">A cancellation token.</param>
    private async Task VerifyReadyMapsAsync(CancellationToken cancellationToken)
    {
        foreach (var key in coordinator.ReadyKeys())
        {
            if (coordinator.Snapshot(key).Ready is not { } ready)
            {
                continue;
            }

            foreach (var (guild, server) in coordinator.Requesters(key))
            {
                if (coordinator.MatchFor(key, guild, server) != RustMapsMapMatch.Unknown)
                {
                    continue;
                }

                var monuments = await query.GetMonumentsAsync(guild, server, cancellationToken)
                    .ConfigureAwait(false);
                var match = RustMapsMapMatcher.Compare(ready.Monuments, monuments, (uint)key.Size);
                if (match == RustMapsMapMatch.Unknown)
                {
                    continue; // Nothing to judge on yet (server offline, or no monuments): retry next tick.
                }

                if (match == RustMapsMapMatch.Mismatch)
                {
                    LogMapMismatch(logger, guild, server, key.Size, key.Seed);
                }

                coordinator.SetMatch(key, guild, server, match);
                await AnnounceAsync(key, guild, server, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task AnnounceAsync(
        RustMapsMapKey key,
        ulong guild,
        Guid server,
        CancellationToken cancellationToken)
    {
        if (_announced.Add((key, guild, server)))
        {
            await eventBus.PublishAsync(new InfoMapReadyEvent(guild, server), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task ConsumeConnectionStatusAsync(
        IAsyncEnumerable<ConnectionStatusChangedEvent> statusEvents,
        CancellationToken cancellationToken)
    {
        try
        {
            await statusEvents.ConsumeAsync(OnConnectionStatusAsync,
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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The RustMaps render for size {Size} seed {Seed} does not match the world guild {GuildId} "
                  + "server {ServerId} is running (custom or pre-generated level); showing the server's own "
                  + "map instead.")]
    private static partial void LogMapMismatch(ILogger logger, ulong guildId, Guid serverId, int size, int seed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling {EventType} failed; skipping that event.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Info-map tick loop faulted.")]
    private static partial void LogTickFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Info-map connection-status loop faulted.")]
    private static partial void LogStatusFaulted(ILogger logger, Exception exception);
}
