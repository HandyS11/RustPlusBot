using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Players.Relaying;

namespace RustPlusBot.Features.Players.Hosting;

/// <summary>Consumes <see cref="PlayerStateChangedEvent"/> and relays each to #events + in-game chat.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="relay">Relays player transitions.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class PlayersHostedService(
    IEventBus eventBus,
    PlayerEventRelay relay,
    ILogger<PlayersHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _loop = Task.Run(() => ConsumeAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
#pragma warning disable VSTHRD003 // Our own loop task, joined on stop.
                await _loop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<PlayerStateChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await relay.RelayAsync(evt, cancellationToken).ConfigureAwait(false);
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
            LogRelayLoopFaulted(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Player relay loop faulted.")]
    private static partial void LogRelayLoopFaulted(ILogger logger, Exception exception);
}
