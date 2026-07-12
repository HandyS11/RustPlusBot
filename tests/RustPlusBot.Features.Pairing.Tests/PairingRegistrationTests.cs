using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Features.Pairing.Accounts;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Features.Pairing.Posting;
using RustPlusBot.Features.Pairing.Supervisor;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class PairingRegistrationTests
{
    [Fact]
    public async Task Services_Resolve()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(Substitute.For<ICredentialProtector>());
        services.AddSingleton(Substitute.For<IUserDmSender>());
        services.AddLogging();
        services.AddBotPersistence("DataSource=:memory:");
        services.AddOptions<PairingOptions>();
        services.AddPairing();

        // Shadow the Discord-backed poster and Workspace-backed locator with substitutes.
        services.AddSingleton(Substitute.For<ISetupChannelLocator>());
        services.AddSingleton(Substitute.For<ISetupChannelPoster>());

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetRequiredService<IPairingSupervisor>());
        Assert.NotNull(provider.GetRequiredService<IServerPairingCoordinator>());
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPairingHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAccountDisconnectService>());
    }
}
