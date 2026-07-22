using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Hosting;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Hosting;

public sealed class WorkspaceConnectionStatusTests
{
    [Fact]
    public async Task ConnectionStatusChanged_ReconcilesThatServer()
    {
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var services = new ServiceCollection();
        services.AddScoped(_ => reconciler);
        // StartAsync now resolves IWorkspaceRegistry up front (see the startup guard test); this
        // minimal container needs a stand-in so that resolution succeeds.
        services.AddSingleton(Substitute.For<IWorkspaceRegistry>());
        await using var provider = services.BuildServiceProvider();

        var bus = new InMemoryEventBus();
        var client = new DiscordSocketClient();
        var service = new WorkspaceHostedService(client, bus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkspaceHostedService>.Instance);

        await service.StartAsync(default);
        var serverId = Guid.NewGuid();
        // The in-process bus does not replay; re-publish until the consumer has reconciled (or deadline).
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !reconciler.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IWorkspaceReconciler.ReconcileServerAsync)))
        {
            await bus.PublishAsync(
                new ConnectionStatusChangedEvent(10UL, serverId, IsConnected: false, WasConnected: false));
            await Task.Delay(20);
        }

        await reconciler.Received().ReconcileServerAsync(10UL, serverId, Arg.Any<CancellationToken>());

        await service.StopAsync(default);
    }

    [Fact]
    public async Task ServerCredentialsChanged_ReconcilesThatServer()
    {
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var services = new ServiceCollection();
        services.AddScoped(_ => reconciler);
        // StartAsync now resolves IWorkspaceRegistry up front (see the startup guard test); this
        // minimal container needs a stand-in so that resolution succeeds.
        services.AddSingleton(Substitute.For<IWorkspaceRegistry>());
        await using var provider = services.BuildServiceProvider();

        var bus = new InMemoryEventBus();
        var client = new DiscordSocketClient();
        var service = new WorkspaceHostedService(client, bus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkspaceHostedService>.Instance);

        await service.StartAsync(default);
        var serverId = Guid.NewGuid();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !reconciler.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IWorkspaceReconciler.ReconcileServerAsync)))
        {
            await bus.PublishAsync(new ServerCredentialsChangedEvent(10UL, serverId));
            await Task.Delay(20);
        }

        await reconciler.Received().ReconcileServerAsync(10UL, serverId, Arg.Any<CancellationToken>());

        await service.StopAsync(default);
    }
}
