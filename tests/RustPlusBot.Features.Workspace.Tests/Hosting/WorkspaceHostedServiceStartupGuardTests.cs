using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Workspace.Hosting;
using RustPlusBot.Persistence;

namespace RustPlusBot.Features.Workspace.Tests.Hosting;

public sealed class WorkspaceHostedServiceStartupGuardTests
{
    [Fact]
    public async Task StartAsync_throws_when_a_gated_capability_has_no_provider()
    {
        // Mirrors a host that composes AddWorkspace() without the module (e.g. AddClans()) that owns
        // the "clan" capability. WorkspaceRegistry's constructor guards against this because the
        // reconciler would otherwise treat the gated channels as unavailable and delete them; this test
        // proves the host now fails fast at StartAsync instead of starting and faulting quietly later.
        var services = new ServiceCollection();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(Substitute.For<IRustServerQuery>());
        services.AddLogging();
        services.AddBotPersistence("DataSource=:memory:");
        services.AddWorkspace();
        // Deliberately no IWorkspaceCapabilityProvider registered for "clan".

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var client = new DiscordSocketClient();
        var hostedService = new WorkspaceHostedService(
            client,
            provider.GetRequiredService<IEventBus>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkspaceHostedService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => hostedService.StartAsync(default));
        Assert.Contains("clan", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
