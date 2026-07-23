using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Discord.Posting;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Players.Hosting;
using RustPlusBot.Features.Players.Relaying;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Players.Tests;

public sealed class PlayerEventRegistrationTests
{
    [Fact]
    public void Services_resolve()
    {
        using var provider = BuildProvider();
        Assert.Contains(provider.GetServices<IHostedService>(), h => h is PlayersHostedService);
        Assert.NotNull(provider.GetRequiredService<PlayerEventRelay>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<RenderGate>();
        services.AddSingleton<DiscordChannelMessenger>();
        services.AddSingleton<IPlayerEventChannelLocator>(Substitute.For<IPlayerEventChannelLocator>());
        services.AddScoped<IWorkspaceStore>(_ => Substitute.For<IWorkspaceStore>());
        services.AddScoped<IMapSettingsStore>(_ => Substitute.For<IMapSettingsStore>());
        services.AddSingleton<IBotTeamChatSender>(Substitute.For<IBotTeamChatSender>());
        services.AddPlayers();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
