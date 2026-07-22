using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Clans.State;

namespace RustPlusBot.Features.Clans.Hosting;

/// <summary>
/// Runs the single clan state consumer loop. Clan chat is not handled here at all: Features.Chat
/// owns both chat bridges, and nothing is ever read back out of #claninfo.
/// </summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="stateService">Applies each observed clan state change.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ClansHostedService(
    IEventBus eventBus,
    ClanStateService stateService,
    ILogger<ClansHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _stateLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stateLoop = Task.Run(() => ConsumeClanStateAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_stateLoop is null)
        {
            return;
        }

        try
        {
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks — this is our own loop task, joined on stop.
            await _stateLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task ConsumeClanStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ClanStateChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                // Per-item isolation: a transient store failure, a Discord 5xx during the transition
                // reconcile or an embed validation throw must cost one event, not the loop. Losing
                // the loop would also strand the teardown path, leaving the clan channels behind
                // for a player who has already left their clan.
                try
                {
                    await stateService.ApplyAsync(evt, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Shutting down: let the outer handler end the loop quietly.
                    throw;
                }
#pragma warning disable CA1031 // Broad catch: one faulted clan state change must not end the loop.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogStateChangeFailed(logger, evt.GuildId, evt.ServerId, ex);
                }
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
            LogStateLoopFaulted(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Clan state loop faulted.")]
    private static partial void LogStateLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Applying a clan state change for guild {GuildId} server {ServerId} failed.")]
    private static partial void LogStateChangeFailed(ILogger logger,
        ulong guildId,
        Guid serverId,
        Exception exception);
}
