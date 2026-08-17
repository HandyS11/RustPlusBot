using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Discord.Posting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.Vending.Indexing;
using RustPlusBot.Features.Vending.Relaying;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Vending;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Validates that <see cref="VendingServiceCollectionExtensions.AddVending"/> registers all required services.</summary>
public sealed class VendingRegistrationTests
{
    /// <summary>
    /// Verifies the container resolves the feature's key services without captive-dependency errors, and
    /// that <see cref="IVendingReadModel"/> and <see cref="VendingIndex"/> resolve to the same singleton
    /// instance — the relay writes through <see cref="VendingIndex"/> and commands read through
    /// <see cref="IVendingReadModel"/>, so two instances would mean poll data is never visible to search.
    /// </summary>
    [Fact]
    public void AddVending_resolves_without_captive_dependency_errors()
    {
        var services = new ServiceCollection();

        // Logging (required by concrete types such as DiscordVendingChannelPoster and VendingHostedService)
        services.AddLogging();

        // Cross-layer singletons from other slices
        services.AddSingleton(Substitute.For<IEventBus>());
        services.AddSingleton(Substitute.For<IVendingChannelLocator>());
        services.AddSingleton(Substitute.For<IItemDatabase>());

        // Discord
        var discordConfig = new DiscordSocketConfig();
        services.AddSingleton(new DiscordSocketClient(discordConfig));
        services.AddSingleton<RenderGate>();
        services.AddSingleton<DiscordChannelMessenger>();

        // Scoped stores from Persistence
        services.AddScoped(_ => Substitute.For<IVendingStore>());
        services.AddScoped(_ => Substitute.For<IMapSettingsStore>());

        services.AddOptions<VendingOptions>();
        services.AddVending();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var readModel = provider.GetRequiredService<IVendingReadModel>();
        var index = provider.GetRequiredService<VendingIndex>();
        using var scope = provider.CreateScope();
        var trackService = scope.ServiceProvider.GetRequiredService<IVendingTrackService>();
        var relay = provider.GetRequiredService<VendingNotificationRelay>();
        var purger = provider.GetRequiredService<VendingWipePurger>();
        var hostedService = provider.GetRequiredService<IHostedService>();

        Assert.NotNull(readModel);
        Assert.NotNull(trackService);
        Assert.NotNull(relay);
        Assert.NotNull(purger);
        Assert.NotNull(hostedService);

        // The assertion this test exists for: both resolve to the same singleton.
        Assert.Same(index, readModel);
    }

    /// <summary>Verifies core service descriptors are present after calling <see cref="VendingServiceCollectionExtensions.AddVending"/>.</summary>
    [Fact]
    public void AddVending_registers_core_services()
    {
        var services = new ServiceCollection();
        services.AddVending();

        Assert.Contains(services, d => d.ServiceType == typeof(ILocalizer));
        Assert.Contains(services, d => d.ServiceType == typeof(VendingIndex));
        Assert.Contains(services, d => d.ServiceType == typeof(IVendingReadModel));
        Assert.Contains(services, d => d.ServiceType == typeof(IVendingTrackService));
        Assert.Contains(services, d => d.ServiceType == typeof(VendingNotificationRelay));
        Assert.Contains(services, d => d.ServiceType == typeof(VendingWipePurger));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
    }
}
