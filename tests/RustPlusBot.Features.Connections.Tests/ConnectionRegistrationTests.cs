using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class ConnectionRegistrationTests
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
        services.AddOptions<ConnectionOptions>();
        services.AddConnections();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Assert.NotNull(provider.GetRequiredService<IConnectionSupervisor>());
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IConnectionStore>());
    }
}
