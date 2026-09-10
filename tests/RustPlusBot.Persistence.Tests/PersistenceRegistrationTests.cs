using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Persistence.Chat;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Credentials;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Persistence.Tests;

public sealed class PersistenceRegistrationTests
{
    [Fact]
    public void AddBotPersistence_RegistersDbContextFactoryAndScopedServices()
    {
        var services = new ServiceCollection();
        services.AddBotPersistence("DataSource=:memory:");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Factory is available (used by the Host for the startup migration).
        Assert.NotNull(provider.GetService<IDbContextFactory<BotDbContext>>());

        // A scoped BotDbContext resolves (so the services that inject it resolve).
        Assert.NotNull(scope.ServiceProvider.GetService<BotDbContext>());

        // Services that depend only on BotDbContext resolve from a scope.
        Assert.NotNull(scope.ServiceProvider.GetService<IServerService>());

        // ICredentialStore is registered here, but its ICredentialProtector dependency is
        // supplied by the Host, so verify the registration descriptor rather than resolving it.
        Assert.Contains(services, d => d.ServiceType == typeof(ICredentialStore));
        Assert.Contains(services, d => d.ServiceType == typeof(IFcmRegistrationStore));
        Assert.Contains(services, d => d.ServiceType == typeof(IConnectionStore));
        Assert.Contains(services, d => d.ServiceType == typeof(ISwitchStore));
        Assert.Contains(services, d => d.ServiceType == typeof(IChatWebhookStore));
    }
}
