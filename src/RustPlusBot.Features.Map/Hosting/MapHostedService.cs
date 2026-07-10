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

/// <summary>Bundles the rendering-pipeline collaborators injected into <see cref="MapHostedService"/>.</summary>
/// <param name="Composer">Renders the map PNG from the cached base + live markers.</param>
/// <param name="Cache">The base-map cache, cleared on disconnect.</param>
/// <param name="Locator">Resolves the #map Discord channel for a server.</param>
/// <param name="Poster">Posts the rendered PNG to Discord.</param>
internal sealed record MapPipeline(
    MapComposer Composer,
    BaseMapCache Cache,
    IMapChannelLocator Locator,
    IMapChannelPoster Poster);

/// <summary>
/// Keeps the #map image current: re-renders on marker changes (including moved markers), on a
/// steady interval (a backstop in case a delta is missed), and on connect; clears the base-map
/// cache on disconnect. All refreshes pass through a per-server throttle so the surfaces never
/// double-post.
/// </summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="pipeline">Bundles the rendering-pipeline collaborators.</param>
/// <param name="clock">Supplies the current time for the throttle.</param>
/// <param name="options">Supplies the refresh interval.</param>
/// <param name="scopeFactory">Opens scopes to read connection state.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class MapHostedService(
    IEventBus eventBus,
    MapPipeline pipeline,
    IClock clock,
    IOptions<MapOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<MapHostedService> logger) : IHostedService, IDisposable
{
    private readonly BaseMapCache _cache = pipeline.Cache;
    private readonly MapComposer _composer = pipeline.Composer;

    /// <summary>A value-less concurrent set of currently-connected servers the periodic loop repaints.</summary>
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), byte> _connected = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly IMapChannelLocator _locator = pipeline.Locator;
    private readonly IMapChannelPoster _poster = pipeline.Poster;
    private readonly MapRefreshThrottle _throttle = new(clock);
    private Task? _markerLoop;
    private Task? _settingsLoop;
    private Task? _statusLoop;
    private Task? _tickLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _markerLoop = Task.Run(() => ConsumeMarkerEventsAsync(_cts.Token), CancellationToken.None);
        _settingsLoop = Task.Run(() => ConsumeSettingsEventsAsync(_cts.Token), CancellationToken.None);
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
                     _markerLoop, _settingsLoop, _statusLoop, _tickLoop
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

    /// <summary>Drains <see cref="MapSettingsChangedEvent"/> from the bus and triggers an immediate repaint.</summary>
    /// <remarks>
    /// Throttle caveat: the immediate toggle repaint passes through the same <see cref="MapRefreshThrottle"/>
    /// as the periodic tick. If a toggle lands inside a recent paint's throttle window, the immediate repaint
    /// is skipped and the change shows on the next tick (≤ <see cref="MapOptions.MapRefreshInterval"/>).
    /// This is intentional — matches 2b's coalescing design.
    /// </remarks>
    /// <param name="cancellationToken">A cancellation token.</param>
    private async Task ConsumeSettingsEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<MapSettingsChangedEvent>(cancellationToken)
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
            LogSettingsLoopFaulted(logger, ex);
        }
    }

    /// <summary>
    /// Repaints every connected server's #map on a steady interval. A moving cargo ship / heli / chinook
    /// now fires a <see cref="MapMarkersChangedEvent"/> "Moved" delta that drives its own refresh; this
    /// tick is a backstop for any missed delta and posts the first image after connect.
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

        var channelId = await _locator.GetChannelIdAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (channelId is not { } id)
        {
            return;
        }

        var png = await _composer.ComposeAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (png is null)
        {
            return;
        }

        await _poster.PostAsync(id, png, cancellationToken).ConfigureAwait(false);
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
                _cache.Clear(evt.GuildId, evt.ServerId);
                return;
            }

            _connected[key] = 0;
        }

        // Post an initial image as soon as the server connects, rather than waiting for the first tick.
        await RefreshAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Map marker loop faulted.")]
    private static partial void LogMarkerLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Map settings loop faulted.")]
    private static partial void LogSettingsLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Map connection-status loop faulted.")]
    private static partial void LogStatusLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Map periodic-refresh loop faulted.")]
    private static partial void LogTickLoopFaulted(ILogger logger, Exception exception);
}
