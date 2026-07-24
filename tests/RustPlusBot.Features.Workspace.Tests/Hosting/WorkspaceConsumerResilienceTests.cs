using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Hosting;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Hosting;

public sealed class WorkspaceConsumerResilienceTests
{
    private static (WorkspaceHostedService Service, ServiceProvider Provider, InMemoryEventBus Bus) Build(
        IWorkspaceReconciler reconciler)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => reconciler);
        services.AddSingleton(Substitute.For<IWorkspaceRegistry>()); // StartAsync resolves this up front.
        var provider = services.BuildServiceProvider();

        var bus = new InMemoryEventBus();
        var service = new WorkspaceHostedService(new DiscordSocketClient(), bus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkspaceHostedService>.Instance);
        return (service, provider, bus);
    }

    /// <summary>
    /// A Discord REST timeout inside the reconcile is routine, but it used to escape the consumer's
    /// await-foreach and end the subscription for the rest of the process — every later connect/disconnect
    /// then left the info channels stale until the bot restarted.
    /// </summary>
    [Fact]
    public async Task ConnectionStatus_consumer_survives_a_faulting_reconcile()
    {
        var calls = 0;
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        reconciler.ReconcileServerAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref calls) == 1
                ? throw new TimeoutException("The operation has timed out.")
                : Task.CompletedTask);

        var (service, provider, bus) = Build(reconciler);
        await using var _ = provider;

        await service.StartAsync(CancellationToken.None);
        try
        {
            // The in-process bus does not replay; re-publish until the consumer has handled two events.
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (DateTimeOffset.UtcNow < deadline && Volatile.Read(ref calls) < 2)
            {
                await bus.PublishAsync(
                    new ConnectionStatusChangedEvent(10UL, Guid.NewGuid(), IsConnected: true, WasConnected: false));
                await Task.Delay(20);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.True(Volatile.Read(ref calls) >= 2,
            $"the consumer stopped after the first reconcile threw (calls: {calls})");
    }
}
