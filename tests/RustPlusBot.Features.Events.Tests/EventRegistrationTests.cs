using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord.Posting;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Hosting;
using RustPlusBot.Features.Events.Relaying;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Events.Tests;

public sealed class EventRegistrationTests
{
    [Fact]
    public void Services_resolve()
    {
        using var provider = BuildProvider();

        Assert.Contains(provider.GetServices<IHostedService>(), h => h is EventsHostedService);

        var eventState = provider.GetRequiredService<IEventState>();
        var eventStateStore = provider.GetRequiredService<EventStateStore>();
        Assert.Same(eventStateStore, eventState);

        Assert.NotNull(provider.GetRequiredService<EventRelay>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(Substitute.For<IClock>());
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<RenderGate>();
        services.AddSingleton<DiscordChannelMessenger>();
        services.AddSingleton<IEventChannelLocator>(Substitute.For<IEventChannelLocator>());
        services.AddScoped<IWorkspaceStore>(_ => Substitute.For<IWorkspaceStore>());
        services.AddScoped<IConnectionStore>(_ => Substitute.For<IConnectionStore>());
        services.AddSingleton<IBotTeamChatSender>(Substitute.For<IBotTeamChatSender>());
        services.AddSingleton(Options.Create(new ConnectionOptions()));
        services.AddEvents();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
