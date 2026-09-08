using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Hosting;
using RustPlusBot.Features.Commands.Dispatching;

namespace RustPlusBot.Features.Commands.Hosting;

/// <summary>Consumes team messages from the bus and dispatches in-game commands.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="scopeFactory">Creates a DI scope per event (the dispatcher uses scoped stores).</param>
/// <param name="logger">The logger.</param>
internal sealed class CommandsHostedService(
    IEventBus eventBus,
    IServiceScopeFactory scopeFactory,
    ILogger<CommandsHostedService> logger) : EventLoopHostedService(eventBus, logger)
{
    /// <inheritdoc />
    protected override IEnumerable<EventLoopRegistration> Loops =>
    [
        Loop<TeamMessageReceivedEvent>("command dispatch", DispatchAsync),
    ];

    private async Task DispatchAsync(TeamMessageReceivedEvent evt, CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<CommandDispatcher>();
            await dispatcher.DispatchAsync(evt, cancellationToken).ConfigureAwait(false);
        }
    }
}
