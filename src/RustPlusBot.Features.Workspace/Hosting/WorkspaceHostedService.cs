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
    private Task? _eventLoop;
    private bool _startupDone;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.Ready += OnReadyAsync;
        client.ChannelDestroyed += OnChannelDestroyedAsync;
        _eventLoop = Task.Run(() => ConsumeServerRegisteredAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        client.Ready -= OnReadyAsync;
        client.ChannelDestroyed -= OnChannelDestroyedAsync;
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_eventLoop is not null)
        {
            try
            {
#pragma warning disable VSTHRD003 // Avoid awaiting or returning a Task representing work that was not started within your context
                await _eventLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    private async Task OnReadyAsync()
    {
        if (_startupDone)
        {
            return;
        }

        _startupDone = true;
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

    private async Task ConsumeServerRegisteredAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var registered in eventBus.SubscribeAsync<ServerRegisteredEvent>(cancellationToken).ConfigureAwait(false))
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
                    await reconciler.ReconcileServerAsync(registered.GuildId, registered.ServerId, cancellationToken).ConfigureAwait(false);
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
