using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Hosting;

/// <summary>Runs startup reconcile, self-heals on channel deletion, and reacts to server registration.</summary>
/// <param name="client">The socket client (for Ready and ChannelDestroyed).</param>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="scopeFactory">Creates scopes for the scoped reconciler/store.</param>
/// <param name="logger">The logger.</param>
internal sealed class WorkspaceHostedService(
    DiscordSocketClient client,
    IEventBus eventBus,
    IServiceScopeFactory scopeFactory,
    ILogger<WorkspaceHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _connectionStatusLoop;
    private Task? _serverCredentialsLoop;
    private Task? _serverRegisteredLoop;
    private bool _startupDone;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.Ready += OnReadyAsync;
        client.ChannelDestroyed += OnChannelDestroyedAsync;
        _serverRegisteredLoop = Task.Run(() => ConsumeServerRegisteredAsync(_cts.Token), CancellationToken.None);
        _connectionStatusLoop = Task.Run(() => ConsumeConnectionStatusAsync(_cts.Token), CancellationToken.None);
        _serverCredentialsLoop = Task.Run(() => ConsumeServerCredentialsAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        client.Ready -= OnReadyAsync;
        client.ChannelDestroyed -= OnChannelDestroyedAsync;
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _serverRegisteredLoop, _connectionStatusLoop, _serverCredentialsLoop
                 })
        {
            if (loop is null)
            {
                continue;
            }

            try
            {
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks — these are our own loop tasks, joined on stop.
                await loop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private Task OnReadyAsync()
    {
        // Ready fires on every gateway (re)connect; only heal once per process. Ready is dispatched
        // serially on the gateway thread, so no synchronization is needed — set the flag before
        // offloading so a re-fired Ready can't double-run.
        if (_startupDone)
        {
            return Task.CompletedTask;
        }

        _startupDone = true;

        // Healing sweeps every provisioned guild's channels over REST; doing it inline blocks the
        // gateway task and stalls event dispatch, so offload it. Failures must be caught here —
        // nothing awaits this.
        _ = Task.Run(HealProvisionedGuildsAsync);
        return Task.CompletedTask;
    }

    private async Task HealProvisionedGuildsAsync()
    {
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
                var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
                foreach (var guildId in await store.GetProvisionedGuildIdsAsync().ConfigureAwait(false))
                {
                    await reconciler.HealGuildAsync(guildId).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) // Broad catch is intentional: a faulting startup heal must not crash the host.
        {
            logger.LogError(ex, "Startup self-heal failed.");
        }
    }

    private async Task OnChannelDestroyedAsync(SocketChannel channel)
    {
        if (channel is not SocketGuildChannel guildChannel)
        {
            return;
        }

        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
                await reconciler.HealGuildAsync(guildChannel.Guild.Id).ConfigureAwait(false);
            }
        }
        catch (Exception ex) // Broad catch is intentional: a faulting self-heal must not crash the host.
        {
            logger.LogError(ex, "Self-heal failed for guild {GuildId}.", guildChannel.Guild.Id);
        }
    }

    private async Task ConsumeConnectionStatusAsync(CancellationToken cancellationToken)
    {
        // If this loop faults (broad catch), the consumer exits permanently and info channels stop
        // updating until the host restarts. Acceptable: the reconciler is idempotent and a restart heals.
        try
        {
            await foreach (var changed in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
                    await reconciler.ReconcileServerAsync(changed.GuildId, changed.ServerId, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex) // Broad catch is intentional: a faulting consumer must not crash the host.
        {
            logger.LogError(ex, "ConnectionStatusChanged consumer faulted.");
        }
    }

    private async Task ConsumeServerCredentialsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var changed in eventBus.SubscribeAsync<ServerCredentialsChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
                    await reconciler.ReconcileServerAsync(changed.GuildId, changed.ServerId, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex) // Broad catch is intentional: a faulting consumer must not crash the host.
        {
            logger.LogError(ex, "ServerCredentialsChanged consumer faulted.");
        }
    }

    private async Task ConsumeServerRegisteredAsync(CancellationToken cancellationToken)
    {
        // Subscription is registered when this loop first calls SubscribeAsync; the in-process bus does
        // not replay, so events published before this point are not delivered. Fine here (the only 1a
        // producer is the runtime-only simulate-server command); a real producer (1b FCM pairing) runs
        // long after startup.
        try
        {
            await foreach (var registered in eventBus.SubscribeAsync<ServerRegisteredEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
                    await reconciler.ReconcileServerAsync(registered.GuildId, registered.ServerId, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex) // Broad catch is intentional: a faulting consumer must not crash the host.
        {
            logger.LogError(ex, "ServerRegistered consumer faulted.");
        }
    }
}
