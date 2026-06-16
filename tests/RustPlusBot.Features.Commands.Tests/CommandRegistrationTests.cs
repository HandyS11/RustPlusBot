using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Hosting;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Persistence.Commands;
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
        services.AddScoped<IMuteStore>(_ => Substitute.For<IMuteStore>());
        services.AddScoped<IWorkspaceStore>(_ => Substitute.For<IWorkspaceStore>());
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
        Assert.Equal(6, handlers.Count);
        Assert.Contains(handlers, h => h.Name == "mute");
        Assert.Contains(handlers, h => h.Name == "pop");
        Assert.Contains(handlers, h => h.Name == "time");
    }
}
