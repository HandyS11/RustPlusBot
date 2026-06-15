using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Supervisor;

namespace RustPlusBot.Features.Connections.Hosting;

/// <summary>Drives the connection supervisor: start all on startup, react to server registration, stop on shutdown.</summary>
/// <param name="supervisor">The connection supervisor.</param>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ConnectionHostedService(
    IConnectionSupervisor supervisor,
    IEventBus eventBus,
    ILogger<ConnectionHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _eventLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _eventLoop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_eventLoop is not null)
        {
            try
            {
#pragma warning disable VSTHRD003 // Suppress: this is our own loop task, joined on stop.
                await _eventLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        await supervisor.StopAllAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // Subscription is registered when this loop first awaits the bus, after StartAllAsync completes.
        // The in-process bus does not replay, so events published before this point are not delivered. Safe:
        // the server-registered event is only raised by the FCM pairing flow at runtime, long after startup.
        try
        {
            await supervisor.StartAllAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var registered in eventBus.SubscribeAsync<ServerRegisteredEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await supervisor.EnsureConnectionAsync(registered.GuildId, registered.ServerId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch is intentional: a faulting consumer must not crash the host.
        catch (Exception ex)
        {
            LogLoopFaulted(logger, ex);
        }
#pragma warning restore CA1031
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Connection hosted-service loop faulted.")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception);
}
