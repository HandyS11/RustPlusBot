using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Features.Pairing.Supervisor;

namespace RustPlusBot.Features.Pairing.Hosting;

/// <summary>Drives the supervisor from the host lifecycle: start listeners on Ready, stop them on shutdown.</summary>
/// <param name="client">The socket client (for the Ready event).</param>
/// <param name="supervisor">The pairing supervisor.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class PairingHostedService(
    DiscordSocketClient client,
    IPairingSupervisor supervisor,
    ILogger<PairingHostedService> logger) : IHostedService
{
    private bool _started;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.Ready += OnReadyAsync;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        client.Ready -= OnReadyAsync;
        await supervisor.StopAllAsync().ConfigureAwait(false);
    }

    private Task OnReadyAsync()
    {
        // Ready fires on every (re)connect; only start listeners once per process. Ready is dispatched
        // serially on the gateway thread, so the plain bool guard needs no synchronization (same pattern
        // as DiscordBotService and WorkspaceHostedService) — set the flag before offloading so a
        // re-fired Ready can't double-start.
        if (_started)
        {
            return Task.CompletedTask;
        }

        _started = true;

        // Starting listeners connects to every active Rust server; doing it inline blocks the gateway
        // task and stalls event dispatch, so offload it. Failures must be caught here — nothing awaits
        // this.
        _ = Task.Run(StartListenersAsync);
        return Task.CompletedTask;
    }

    private async Task StartListenersAsync()
    {
        try
        {
            await supervisor.StartAllActiveAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Broad catch is intentional: a failed startup must not crash the host.
        catch (Exception ex)
        {
            LogStartupFailed(ex);
        }
#pragma warning restore CA1031
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to start FCM pairing listeners.")]
    private partial void LogStartupFailed(Exception exception);
}
