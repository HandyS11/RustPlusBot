using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Workspace.Hosting;

/// <summary>
///     Re-renders every connected server's #info embeds on a steady interval. In-game time, population
///     and team state change continuously, and the workspace reconciler is purely event-driven — without
///     this tick the embeds would only update on connect, disconnect, wipe or credential change.
///     Each tick enumerates the connectable servers from the store (the source of truth) rather than
///     tracking connections from <c>ConnectionStatusChangedEvent</c>: the connection can publish its
///     "connected" event before this service has subscribed on startup, so an event-only set would miss
///     it and that server's embeds would never refresh. A disconnected server renders cheaply — the live
///     queries return null without a socket round-trip, producing its "not connected" embeds.
/// </summary>
/// <param name="options">Supplies the refresh interval.</param>
/// <param name="scopeFactory">Opens a scope per tick (the store) and per refresh (the refresher is scoped).</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ServerInfoRefreshHostedService(
    IOptions<WorkspaceOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<ServerInfoRefreshHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _tickLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _tickLoop = Task.Run(() => RunPeriodicRefreshAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_tickLoop is null)
        {
            return;
        }

        try
        {
            await _tickLoop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
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

    /// <summary>
    ///     Enumerates the connectable servers from the store and refreshes each. A transient enumeration
    ///     failure is logged and swallowed so the next tick still runs; per-server failures are isolated
    ///     inside <see cref="RefreshAsync" />.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when every connectable server has been refreshed.</returns>
    internal async Task RefreshDueServersAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<(ulong GuildId, Guid ServerId)> servers;
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
                servers = await store.ListConnectableServersAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a transient store failure must not stop future ticks.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogTickLoopFaulted(logger, ex);
            return;
        }

        foreach (var (guildId, serverId) in servers)
        {
            await RefreshAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunPeriodicRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(ResolveInterval(options.Value), cancellationToken).ConfigureAwait(false);
                await RefreshDueServersAsync(cancellationToken).ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "#info refresh tick faulted.")]
    private static partial void LogTickLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "#info refresh failed for server {ServerId}.")]
    private static partial void LogRefreshFaulted(ILogger logger, Exception exception, Guid serverId);
}
