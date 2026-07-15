using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Announcing;
using RustPlusBot.Features.Wipes.Detection;

namespace RustPlusBot.Features.Wipes.Hosting;

/// <summary>Runs the wipe-check loop (on connected transitions) and the wipe-announcement loop.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="detector">Diffs the live server against the persisted baseline.</param>
/// <param name="announcer">Posts the wipe announcement in #events.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class WipesHostedService(
    IEventBus eventBus,
    IWipeDetector detector,
    IWipeAnnouncer announcer,
    ILogger<WipesHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _statusLoop;
    private Task? _wipedLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _statusLoop = Task.Run(() => ConsumeStatusAsync(_cts.Token), CancellationToken.None);
        _wipedLoop = Task.Run(() => ConsumeWipedAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _statusLoop, _wipedLoop
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

    private async Task ConsumeStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                if (!evt.IsConnected)
                {
                    continue;
                }

                await detector.CheckAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
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

    private async Task ConsumeWipedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ServerWipedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await announcer.HandleServerWipedAsync(evt, cancellationToken).ConfigureAwait(false);
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
            LogWipedLoopFaulted(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Wipe connection-status loop faulted.")]
    private static partial void LogStatusLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Wipe announcement loop faulted.")]
    private static partial void LogWipedLoopFaulted(ILogger logger, Exception exception);
}
