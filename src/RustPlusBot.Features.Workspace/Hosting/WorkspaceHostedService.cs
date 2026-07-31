using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
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
    private Task? _infoMapReadyLoop;
    private Task? _serverCredentialsLoop;
    private Task? _serverRegisteredLoop;
    private bool _startupDone;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Force the workspace registry's construction now, synchronously, before any heal work is
        // queued. Its constructor throws when a channel spec names a capability with no registered
        // provider. Every other place below resolves it lazily inside a broad catch, so a misconfigured
        // host would otherwise start cleanly and only fault quietly on the first reconcile. Resolving it
        // here, outside any try or catch, lets that exception propagate out of this method so the host
        // genuinely fails to start instead.
        using (var scope = scopeFactory.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();
        }

        client.Ready += OnReadyAsync;
        client.ChannelDestroyed += OnChannelDestroyedAsync;
        _serverRegisteredLoop = Task.Run(() => ConsumeServerRegisteredAsync(_cts.Token), CancellationToken.None);
        _connectionStatusLoop = Task.Run(() => ConsumeConnectionStatusAsync(_cts.Token), CancellationToken.None);
        _serverCredentialsLoop = Task.Run(() => ConsumeServerCredentialsAsync(_cts.Token), CancellationToken.None);
        _infoMapReadyLoop = Task.Run(() => ConsumeInfoMapReadyAsync(_cts.Token), CancellationToken.None);
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
                     _serverRegisteredLoop, _connectionStatusLoop, _serverCredentialsLoop, _infoMapReadyLoop
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
        _ = Task.Run(HealProvisionedGuildsAsync, _cts.Token);
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
                foreach (var guildId in await store.GetProvisionedGuildIdsAsync(_cts.Token).ConfigureAwait(false))
                {
                    await reconciler.HealGuildAsync(guildId, _cts.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
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
                await reconciler.HealGuildAsync(guildChannel.Guild.Id, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex) // Broad catch is intentional: a faulting self-heal must not crash the host.
        {
            logger.LogError(ex, "Self-heal failed for guild {GuildId}.", guildChannel.Guild.Id);
        }
    }

    /// <summary>
    /// Reconciles one server in its own scope. Every consumer below runs this through
    /// <see cref="EventBusConsumption.ConsumeAsync{TEvent}"/>, which absorbs its failures: the reconcile
    /// talks to Discord over REST, where a timeout or a 5xx is routine, and one of those must never end
    /// the subscription that drives the channels.
    /// </summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server to reconcile.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the reconcile has run.</returns>
    private async Task ReconcileServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
            await reconciler.ReconcileServerAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ConsumeConnectionStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.ConsumeAsync<ConnectionStatusChangedEvent>(
                (evt, ct) => ReconcileServerAsync(evt.GuildId, evt.ServerId, ct),
                ex => logger.LogError(ex, "Handling {EventType} failed; skipping that reconcile.",
                    nameof(ConnectionStatusChangedEvent)),
                cancellationToken).ConfigureAwait(false);
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
            await eventBus.ConsumeAsync<ServerCredentialsChangedEvent>(
                (evt, ct) => ReconcileServerAsync(evt.GuildId, evt.ServerId, ct),
                ex => logger.LogError(ex, "Handling {EventType} failed; skipping that reconcile.",
                    nameof(ServerCredentialsChangedEvent)),
                cancellationToken).ConfigureAwait(false);
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

    private async Task ConsumeInfoMapReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.ConsumeAsync<InfoMapReadyEvent>(
                (evt, ct) => ReconcileServerAsync(evt.GuildId, evt.ServerId, ct),
                ex => logger.LogError(ex, "Handling {EventType} failed; skipping that reconcile.",
                    nameof(InfoMapReadyEvent)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex) // Broad catch is intentional: a faulting consumer must not crash the host.
        {
            logger.LogError(ex, "InfoMapReady consumer faulted.");
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
            await eventBus.ConsumeAsync<ServerRegisteredEvent>(
                (evt, ct) => ReconcileServerAsync(evt.GuildId, evt.ServerId, ct),
                ex => logger.LogError(ex, "Handling {EventType} failed; skipping that reconcile.",
                    nameof(ServerRegisteredEvent)),
                cancellationToken).ConfigureAwait(false);
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
