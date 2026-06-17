using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Hosting;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Commands.Servers;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Persistence.Commands;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Tests;

public sealed class CommandRegistrationTests
{
    [Fact]
    public void Dispatcher_and_handlers_resolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(Substitute.For<IClock>());
        services.AddSingleton<IEventBus>(Substitute.For<IEventBus>());
        services.AddSingleton<ITeamChatSender>(Substitute.For<ITeamChatSender>());
        services.AddSingleton<IRustServerQuery>(Substitute.For<IRustServerQuery>());
        services.AddSingleton<IEventState>(_ => Substitute.For<IEventState>());
        services.AddSingleton<IRigState>(_ => Substitute.For<IRigState>());
        services.AddScoped<IMuteStore>(_ => Substitute.For<IMuteStore>());
        services.AddScoped<IWorkspaceStore>(_ => Substitute.For<IWorkspaceStore>());
        services.AddScoped<IServerService>(_ => Substitute.For<IServerService>());
        services.AddOptions<CommandOptions>();
        services.AddCommands();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.Contains(provider.GetServices<IHostedService>(), h => h is CommandsHostedService);

        // The singletons AddCommands wires must each resolve (guards against an accidentally dropped registration).
        Assert.NotNull(provider.GetRequiredService<CommandCooldown>());
        Assert.NotNull(provider.GetRequiredService<BotUptime>());
        Assert.NotNull(provider.GetRequiredService<ICommandLocalizer>());

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CommandDispatcher>());
        var handlers = scope.ServiceProvider.GetServices<ICommandHandler>().ToList();
        Assert.Equal(18, handlers.Count);
        Assert.Contains(handlers, h => h.Name == "mute");
        Assert.Contains(handlers, h => h.Name == "pop");
        Assert.Contains(handlers, h => h.Name == "time");
        Assert.Contains(handlers, h => h.Name == "wipe");
        Assert.Contains(handlers, h => h.Name == "online");
        Assert.Contains(handlers, h => h.Name == "offline");
        Assert.Contains(handlers, h => h.Name == "team");
        Assert.Contains(handlers, h => h.Name == "steamid");
        Assert.Contains(handlers, h => h.Name == "alive");
        Assert.Contains(handlers, h => h.Name == "prox");
        Assert.Contains(handlers, h => h.Name == "cargo");
        Assert.Contains(handlers, h => h.Name == "heli");
        Assert.Contains(handlers, h => h.Name == "chinook");
        Assert.Contains(handlers, h => h.Name == "events");
        Assert.Contains(handlers, h => h.Name == "small");
        Assert.Contains(handlers, h => h.Name == "large");
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ServerResolver>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ServerQueryService>());
    }

    [Fact]
    public void Commands_contribute_an_interaction_module_assembly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(Substitute.For<IClock>());
        services.AddSingleton<IEventBus>(Substitute.For<IEventBus>());
        services.AddSingleton<ITeamChatSender>(Substitute.For<ITeamChatSender>());
        services.AddSingleton<IRustServerQuery>(Substitute.For<IRustServerQuery>());
        services.AddSingleton<IEventState>(_ => Substitute.For<IEventState>());
        services.AddSingleton<IRigState>(_ => Substitute.For<IRigState>());
        services.AddScoped<IMuteStore>(_ => Substitute.For<IMuteStore>());
        services.AddScoped<IWorkspaceStore>(_ => Substitute.For<IWorkspaceStore>());
        services.AddScoped<IServerService>(_ => Substitute.For<IServerService>());
        services.AddOptions<CommandOptions>();
        services.AddCommands();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var assemblies = provider.GetServices<InteractionModuleAssembly>();
        Assert.Contains(assemblies, a => a.Assembly == typeof(CommandServiceCollectionExtensions).Assembly);
    }
}
