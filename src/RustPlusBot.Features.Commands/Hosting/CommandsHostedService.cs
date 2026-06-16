using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Commands.Dispatching;

namespace RustPlusBot.Features.Commands.Hosting;

/// <summary>Consumes team messages from the bus and dispatches in-game commands.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="scopeFactory">Creates a DI scope per event (the dispatcher uses scoped stores).</param>
/// <param name="logger">The logger.</param>
internal sealed partial class CommandsHostedService(
    IEventBus eventBus,
    IServiceScopeFactory scopeFactory,
    ILogger<CommandsHostedService> logger) : IHostedService, IDisposable
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
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks — this is our own loop task, joined on stop.
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
            await foreach (var evt in eventBus.SubscribeAsync<TeamMessageReceivedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                try
                {
                    var scope = scopeFactory.CreateAsyncScope();
                    await using (scope.ConfigureAwait(false))
                    {
                        var dispatcher = scope.ServiceProvider.GetRequiredService<CommandDispatcher>();
                        await dispatcher.DispatchAsync(evt, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Broad catch: one bad event must not kill the loop.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogDispatchFaulted(logger, ex);
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
            LogLoopFaulted(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Command dispatch faulted for one event.")]
    private static partial void LogDispatchFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Command consume loop faulted.")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception);
}
