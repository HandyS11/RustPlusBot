using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence;

namespace RustPlusBot.Features.Workspace.Tests;

public sealed class WorkspaceRegistrationTests
{
    [Fact]
    public void ReconcilerAndTeardown_ResolveFromScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(Substitute.For<IRustServerQuery>());
        services.AddLogging();
        services.AddBotPersistence("DataSource=:memory:");
        services.AddWorkspace();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkspaceTeardownService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IServerWorkspaceRemover>());
    }

    [Fact]
    public void AddWorkspace_registers_channel_locators()
    {
        var services = new ServiceCollection();
        services.AddWorkspace();

        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IChatChannelLocator)));
        Assert.Contains(services, d => d.ServiceType == typeof(IEventChannelLocator));
        Assert.Contains(services, d => d.ServiceType == typeof(IMapChannelLocator));
        Assert.Contains(services, d => d.ServiceType == typeof(ISwitchChannelLocator));
        Assert.Contains(services, d => d.ServiceType == typeof(IAlarmChannelLocator));
        Assert.Contains(services, d => d.ServiceType == typeof(IStorageMonitorChannelLocator));
        Assert.Contains(services, d => d.ServiceType == typeof(ISetupChannelLocator));
    }
}
