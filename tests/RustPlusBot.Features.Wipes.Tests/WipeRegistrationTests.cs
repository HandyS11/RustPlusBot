using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Announcing;
using RustPlusBot.Features.Wipes.Detection;
using RustPlusBot.Features.Wipes.Posting;
using RustPlusBot.Features.Wipes.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Wipes;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Wipes.Tests;

/// <summary>Validates that <see cref="WipeServiceCollectionExtensions.AddWipes"/> registers all required services.</summary>
public sealed class WipeRegistrationTests
{
    /// <summary>Verifies core service descriptors are present after calling <see cref="WipeServiceCollectionExtensions.AddWipes"/>.</summary>
    [Fact]
    public void AddWipes_registers_core_services()
    {
        var services = new ServiceCollection();
        services.AddWipes();

        Assert.Contains(services, d => d.ServiceType == typeof(ILocalizer));
        Assert.Contains(services, d => d.ServiceType == typeof(IWipeDetector));
        Assert.Contains(services, d => d.ServiceType == typeof(WipeEmbedRenderer));
        Assert.Contains(services, d => d.ServiceType == typeof(IWipeChannelPoster));
        Assert.Contains(services, d => d.ServiceType == typeof(IWipeAnnouncer));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
    }

    /// <summary>Verifies the container resolves key types without captive-dependency errors when all cross-layer deps are provided.</summary>
    [Fact]
    public void AddWipes_resolves_without_captive_dependency_errors()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(Substitute.For<IEventBus>());
        services.AddSingleton(Substitute.For<IRustServerQuery>());
        services.AddSingleton(Substitute.For<IEventChannelLocator>());
        services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig()));
        services.AddScoped(_ => Substitute.For<IWipeBaselineStore>());
        services.AddScoped(_ => Substitute.For<IWorkspaceStore>());

        services.AddWipes();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.NotNull(provider.GetRequiredService<IWipeDetector>());
        Assert.NotNull(provider.GetRequiredService<WipeEmbedRenderer>());
        Assert.NotNull(provider.GetRequiredService<IWipeChannelPoster>());
        Assert.NotNull(provider.GetRequiredService<IWipeAnnouncer>());
        Assert.NotNull(provider.GetRequiredService<IHostedService>());
    }
}
