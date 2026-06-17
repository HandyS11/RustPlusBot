using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Posting;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Map.Hosting;

/// <summary>
/// Keeps the #map image current: re-renders on marker changes, on a steady interval (so moving
/// markers track even though their ids are stable), and on connect; clears the base-map cache on
/// disconnect. All refreshes pass through a per-server throttle so the surfaces never double-post.
/// </summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="composer">Renders the map PNG from the cached base + live markers.</param>
/// <param name="cache">The base-map cache, cleared on disconnect.</param>
/// <param name="locator">Resolves the #map Discord channel for a server.</param>
/// <param name="poster">Posts the rendered PNG to Discord.</param>
/// <param name="clock">Supplies the current time for the throttle.</param>
/// <param name="options">Supplies the refresh interval.</param>
/// <param name="scopeFactory">Opens scopes to read connection state.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class MapHostedService(
    IEventBus eventBus,
    MapComposer composer,
    BaseMapCache cache,
    IMapChannelLocator locator,
    IMapChannelPoster poster,
    IClock clock,
    IOptions<MapOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<MapHostedService> logger) : IHostedService, IDisposable
{
    /// <summary>A value-less concurrent set of currently-connected servers the periodic loop repaints.</summary>
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), byte> _connected = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly MapRefreshThrottle _throttle = new(clock);
    private Task? _markerLoop;
    private Task? _statusLoop;
    private Task? _tickLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _markerLoop = Task.Run(() => ConsumeMarkerEventsAsync(_cts.Token), CancellationToken.None);
        _statusLoop = Task.Run(() => ConsumeConnectionStatusEventsAsync(_cts.Token), CancellationToken.None);
        _tickLoop = Task.Run(() => RunPeriodicRefreshAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _markerLoop, _statusLoop, _tickLoop
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

    private async Task ConsumeMarkerEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<MapMarkersChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await RefreshAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
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
            LogMarkerLoopFaulted(logger, ex);
        }
    }

    /// <summary>
    /// Repaints every connected server's #map on a steady interval. Marker ids are stable, so a moving
    /// cargo ship / heli / chinook fires no <see cref="MapMarkersChangedEvent"/>; this tick is what keeps
    /// their positions current and posts the first image after connect.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the loop stops.</returns>
    private async Task RunPeriodicRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(options.Value.MapRefreshInterval, cancellationToken).ConfigureAwait(false);
                foreach (var (guild, server) in _connected.Keys)
                {
                    await RefreshAsync(guild, server, cancellationToken).ConfigureAwait(false);
                }
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
            LogTickLoopFaulted(logger, ex);
        }
    }

    private async Task RefreshAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        if (!_throttle.ShouldRefresh(guildId, serverId, options.Value.MapRefreshInterval))
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (channelId is not { } id)
        {
            return;
        }

        var png = await composer.ComposeAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (png is null)
        {
            return;
        }

        await poster.PostAsync(id, png, cancellationToken).ConfigureAwait(false);
    }

    private async Task ConsumeConnectionStatusEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await OnConnectionStatusAsync(evt, cancellationToken).ConfigureAwait(false);
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

    private async Task OnConnectionStatusAsync(ConnectionStatusChangedEvent evt, CancellationToken cancellationToken)
    {
        var key = (evt.GuildId, evt.ServerId);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var connectionStore = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var state = await connectionStore.GetStateAsync(evt.GuildId, evt.ServerId, cancellationToken)
                .ConfigureAwait(false);
            if (state is null || state.Status != ConnectionStatus.Connected)
            {
                _connected.TryRemove(key, out _);
                cache.Clear(evt.GuildId, evt.ServerId);
                return;
            }

            _connected[key] = 0;
        }

        // Post an initial image as soon as the server connects, rather than waiting for the first tick.
        await RefreshAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Map marker loop faulted.")]
    private static partial void LogMarkerLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Map connection-status loop faulted.")]
    private static partial void LogStatusLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Map periodic-refresh loop faulted.")]
    private static partial void LogTickLoopFaulted(ILogger logger, Exception exception);
}
