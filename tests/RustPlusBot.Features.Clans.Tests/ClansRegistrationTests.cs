using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord;
using RustPlusBot.Features.Clans.Hosting;
using RustPlusBot.Features.Clans.Messages;
using RustPlusBot.Features.Clans.Modules;
using RustPlusBot.Features.Clans.Names;
using RustPlusBot.Features.Clans.Posting;
using RustPlusBot.Features.Clans.State;
using RustPlusBot.Features.Clans.Writing;
using RustPlusBot.Features.Workspace;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Clans;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Clans.Tests;

public sealed class ClansRegistrationTests
{
    [Fact]
    public async Task Resolves_every_registered_clan_service()
    {
        await using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<ClanStateService>());
        Assert.NotNull(provider.GetRequiredService<ClanChangeRenderer>());
        Assert.NotNull(provider.GetRequiredService<IClanFeedPoster>());
        Assert.NotNull(provider.GetRequiredService<ClanCapabilityProvider>());
        Assert.Contains(provider.GetServices<IHostedService>(), h => h is ClansHostedService);

        // Scoped services must resolve from a scope; ValidateScopes proves no singleton captured one.
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IClanNameResolver>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IClanMotdWriter>());
    }

    [Fact]
    public async Task Registers_the_clan_capability_provider()
    {
        await using var provider = BuildProvider();

        var providers = provider.GetServices<IWorkspaceCapabilityProvider>().ToList();

        // Exactly one: WorkspaceRegistry indexes providers with ToDictionary and throws on a duplicate key.
        Assert.Single(providers, p => string.Equals(p.Capability, "clan", StringComparison.Ordinal));

        // The interface registration and the concrete registration must be the same instance, so the
        // state service's cache invalidation is seen by the registry's provider.
        Assert.Same(provider.GetRequiredService<ClanCapabilityProvider>(), providers[0]);
    }

    [Fact]
    public async Task Registers_the_three_clan_message_renderers()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();

        var keys = scope.ServiceProvider.GetServices<IMessageRenderer>().Select(r => r.MessageKey).ToList();

        Assert.Contains("clan.overview", keys, StringComparer.Ordinal);
        Assert.Contains("clan.roster", keys, StringComparer.Ordinal);
        Assert.Contains("clan.invites", keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Registers_the_assembly_that_carries_the_clan_interaction_modules()
    {
        await using var provider = BuildProvider();

        // Without this the interaction service never discovers ClanMotdModule and the Set MOTD
        // button silently stops responding.
        Assert.Contains(
            provider.GetServices<InteractionModuleAssembly>(),
            a => a.Assembly == typeof(ClanMotdModule).Assembly);
    }

    [Fact]
    public void The_composed_host_registry_covers_every_gated_channel_capability()
    {
        // WorkspaceRegistry refuses to build when a ChannelSpec names a capability no provider
        // answers for, because the reconciler would otherwise delete the gated channels. The clan
        // specs live in Features.Workspace and the provider in Features.Clans, so this pins that the
        // real composition of the two satisfies the guard.
        var services = new ServiceCollection();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(Substitute.For<IRustServerQuery>());
        services.AddLogging();
        services.AddBotPersistence("DataSource=:memory:");
        services.AddWorkspace();
        services.AddClans();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetRequiredService<IWorkspaceRegistry>());
    }

    [Fact]
    public void Does_not_register_a_second_chat_bridge()
    {
        // Features.Chat owns both the team and clan chat bridges; a relay, webhook poster or dedup
        // buffer registered here would double-post every clan chat line.
        var services = new ServiceCollection();
        services.AddClans();

        Assert.DoesNotContain(services, d =>
            d.ServiceType.Name.Contains("Chat", StringComparison.Ordinal) ||
            (d.ImplementationType?.Name.Contains("Chat", StringComparison.Ordinal) ?? false));
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(Substitute.For<IClanInfoChannelLocator>());
        services.AddLogging();

        // Scoped in production; registered scoped here so ValidateScopes catches a captive dependency
        // on either singleton (the state service and the capability provider both need the store).
        services.AddScoped(_ => Substitute.For<IClanStore>());
        services.AddScoped(_ => Substitute.For<IWorkspaceStore>());
        services.AddScoped(_ => Substitute.For<IConnectionStore>());
        services.AddScoped(_ => Substitute.For<IWorkspaceReconciler>());
        services.AddScoped(_ => Substitute.For<IRustServerQuery>());

        services.AddClans();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
    }
}
