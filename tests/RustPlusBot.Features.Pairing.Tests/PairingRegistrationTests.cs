using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Features.Pairing;
using RustPlusBot.Features.Pairing.Accounts;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Features.Pairing.Supervisor;
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

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetRequiredService<IPairingSupervisor>());
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPairingHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAccountDisconnectService>());
    }
}
