using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Reconciler;

namespace RustPlusBot.Features.Workspace.Hosting;

/// <summary>The set of servers currently believed connected, maintained from connection status events.</summary>
internal sealed class ConnectedServerSet
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), byte> _connected = new();

    /// <summary>Adds or removes a server from the refresh rotation.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="connected">True to track the server; false to drop it.</param>
    public void Set(ulong guildId, Guid serverId, bool connected)
    {
        if (connected)
        {
            _connected[(guildId, serverId)] = 0;
        }
        else
        {
            _connected.TryRemove((guildId, serverId), out _);
        }
    }

    /// <summary>A point-in-time copy of the tracked servers.</summary>
    /// <returns>The currently-tracked (guild, server) pairs.</returns>
    public IReadOnlyList<(ulong Guild, Guid Server)> Snapshot() => [.. _connected.Keys];
}

/// <summary>
///     Re-renders every connected server's #info embeds on a steady interval. In-game time, population
///     and team state change continuously, and the workspace reconciler is purely event-driven — without
///     this tick the embeds would only update on connect, disconnect, wipe or credential change.
/// </summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="options">Supplies the refresh interval.</param>
/// <param name="scopeFactory">Opens a scope per refresh (the refresher is scoped).</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ServerInfoRefreshHostedService(
    IEventBus eventBus,
    IOptions<WorkspaceOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<ServerInfoRefreshHostedService> logger) : IHostedService, IDisposable
{
    private readonly ConnectedServerSet _connected = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _statusLoop;
    private Task? _tickLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _statusLoop = Task.Run(() => ConsumeStatusEventsAsync(_cts.Token), CancellationToken.None);
        _tickLoop = Task.Run(() => RunPeriodicRefreshAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _statusLoop, _tickLoop
                 })
        {
            if (loop is null)
            {
                continue;
            }

            try
            {
                await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
        }
    }

    /// <summary>Resolves the effective interval, flooring it so a misconfiguration cannot spin the loop.</summary>
    /// <param name="options">The bound workspace options.</param>
    /// <returns>The interval to sleep between ticks.</returns>
    public static TimeSpan ResolveInterval(WorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var floor = TimeSpan.FromSeconds(1);
        return options.InfoRefreshInterval < floor ? floor : options.InfoRefreshInterval;
    }

    private async Task ConsumeStatusEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                _connected.Set(evt.GuildId, evt.ServerId, evt.IsConnected);
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

    private async Task RunPeriodicRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(ResolveInterval(options.Value), cancellationToken).ConfigureAwait(false);
                foreach (var (guildId, serverId) in _connected.Snapshot())
                {
                    await RefreshAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
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
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var refresher = scope.ServiceProvider.GetRequiredService<IServerInfoRefresher>();
                await refresher.RefreshAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: one server's failure must not stop the rotation.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRefreshFaulted(logger, ex, serverId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "#info status loop faulted.")]
    private static partial void LogStatusLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "#info refresh tick faulted.")]
    private static partial void LogTickLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "#info refresh failed for server {ServerId}.")]
    private static partial void LogRefreshFaulted(ILogger logger, Exception exception, Guid serverId);
}
