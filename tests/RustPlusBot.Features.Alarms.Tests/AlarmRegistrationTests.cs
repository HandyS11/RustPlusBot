using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Alarms.Pairing;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Alarms.Relaying;
using RustPlusBot.Features.Alarms.Rendering;
using RustPlusBot.Discord.Posting;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Alarms;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Alarms.Tests;

/// <summary>Validates that <see cref="AlarmServiceCollectionExtensions.AddAlarms"/> registers all required services.</summary>
public sealed class AlarmRegistrationTests
{
    /// <summary>Verifies core service descriptors are present after calling <see cref="AlarmServiceCollectionExtensions.AddAlarms"/>.</summary>
    [Fact]
    public void AddAlarms_registers_core_services()
    {
        var services = new ServiceCollection();
        services.AddAlarms();

        Assert.Contains(services, d => d.ServiceType == typeof(ILocalizer));
        Assert.Contains(services, d => d.ServiceType == typeof(AlarmEmbedRenderer));
        Assert.Contains(services, d => d.ServiceType == typeof(IAlarmChannelPoster));
        Assert.Contains(services, d => d.ServiceType == typeof(IAlarmRefresher));
        Assert.Contains(services, d => d.ServiceType == typeof(AlarmPairingCoordinator));
        Assert.Contains(services, d => d.ServiceType == typeof(AlarmStateRelay));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
    }

    /// <summary>Verifies the container resolves key types without captive-dependency errors when all cross-layer deps are provided.</summary>
    [Fact]
    public void AddAlarms_resolves_without_captive_dependency_errors()
    {
        var services = new ServiceCollection();

        // Logging (required by concrete types such as DiscordAlarmChannelPoster and AlarmsHostedService)
        services.AddLogging();

        // Cross-layer singletons from other slices
        services.AddSingleton(Substitute.For<IClock>());
        services.AddSingleton(Substitute.For<IEventBus>());
        services.AddSingleton(Substitute.For<IAlarmChannelLocator>());
        services.AddSingleton(Substitute.For<IBotTeamChatSender>());

        // Discord
        var discordConfig = new DiscordSocketConfig();
        services.AddSingleton(new DiscordSocketClient(discordConfig));
        services.AddSingleton<RenderGate>();
        services.AddSingleton<DiscordChannelMessenger>();

        // Scoped stores from Persistence
        services.AddScoped(_ => Substitute.For<IAlarmStore>());
        services.AddScoped(_ => Substitute.For<IWorkspaceStore>());
        services.AddScoped(_ => Substitute.For<IConnectionStore>());

        services.AddAlarms();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var localizer = provider.GetRequiredService<ILocalizer>();
        var renderer = provider.GetRequiredService<AlarmEmbedRenderer>();
        var poster = provider.GetRequiredService<IAlarmChannelPoster>();
        var refresher = provider.GetRequiredService<IAlarmRefresher>();
        var coordinator = provider.GetRequiredService<AlarmPairingCoordinator>();
        var relay = provider.GetRequiredService<AlarmStateRelay>();
        var hostedService = provider.GetRequiredService<IHostedService>();

        Assert.NotNull(localizer);
        Assert.NotNull(renderer);
        Assert.NotNull(poster);
        Assert.NotNull(refresher);
        Assert.NotNull(coordinator);
        Assert.NotNull(relay);
        Assert.NotNull(hostedService);
    }
}
