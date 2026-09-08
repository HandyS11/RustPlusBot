using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Hosting;
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
    ILogger<WorkspaceHostedService> logger) : EventLoopHostedService(eventBus, logger)
{
    private bool _startupDone;

    /// <inheritdoc />
    protected override IEnumerable<EventLoopRegistration> Loops =>
    [
        Loop<ServerRegisteredEvent>("workspace server-registered reconcile",
            (evt, ct) => ReconcileServerAsync(evt.GuildId, evt.ServerId, ct)),
        Loop<ConnectionStatusChangedEvent>("workspace connection-status reconcile",
            (evt, ct) => ReconcileServerAsync(evt.GuildId, evt.ServerId, ct)),
        Loop<ServerCredentialsChangedEvent>("workspace server-credentials reconcile",
            (evt, ct) => ReconcileServerAsync(evt.GuildId, evt.ServerId, ct)),
        Loop<InfoMapReadyEvent>("workspace info-map-ready reconcile",
            (evt, ct) => ReconcileServerAsync(evt.GuildId, evt.ServerId, ct)),
    ];

    /// <inheritdoc />
    protected override void OnStarting()
    {
        // Force the workspace registry's construction now, synchronously, before any heal work is
        // queued. Its constructor throws when a channel spec names a capability with no registered
        // provider. Every other place below resolves it lazily inside a broad catch, so a misconfigured
        // host would otherwise start cleanly and only fault quietly on the first reconcile. Resolving it
        // here, outside any try or catch, lets that exception propagate out of StartAsync so the host
        // genuinely fails to start instead.
        using (var scope = scopeFactory.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IWorkspaceRegistry>();
        }

        client.Ready += OnReadyAsync;
        client.ChannelDestroyed += OnChannelDestroyedAsync;
    }

    /// <inheritdoc />
    protected override void OnStopping()
    {
        client.Ready -= OnReadyAsync;
        client.ChannelDestroyed -= OnChannelDestroyedAsync;
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
        _ = Task.Run(HealProvisionedGuildsAsync, StoppingToken);
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
                foreach (var guildId in await store.GetProvisionedGuildIdsAsync(StoppingToken)
                             .ConfigureAwait(false))
                {
                    await reconciler.HealGuildAsync(guildId, StoppingToken).ConfigureAwait(false);
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
                await reconciler.HealGuildAsync(guildChannel.Guild.Id, StoppingToken).ConfigureAwait(false);
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
    /// Reconciles one server in its own scope. Every loop above runs this through the base class's
    /// consumption, which absorbs its failures: the reconcile talks to Discord over REST, where a timeout
    /// or a 5xx is routine, and one of those must never end the subscription that drives the channels.
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
}
