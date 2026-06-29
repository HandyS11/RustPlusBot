using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Commands.Help;
using RustPlusBot.Features.Commands.Hosting;
using RustPlusBot.Features.Commands.Leader;
using RustPlusBot.Features.Commands.Servers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands;

/// <summary>DI registration for the in-game command framework.</summary>
public static class CommandServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher, cooldown, localizer, handlers, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<CommandCooldown>();
        services.AddSingleton<BotUptime>();
        services.AddRustPlusBotLocalization();
        services.AddItemData();

        services.AddScoped<ICommandHandler, MuteCommandHandler>();
        services.AddScoped<ICommandHandler, UnmuteCommandHandler>();
        services.AddScoped<ICommandHandler, UptimeCommandHandler>();
        services.AddScoped<ICommandHandler, PopCommandHandler>();
        services.AddScoped<ICommandHandler, WipeCommandHandler>();
        services.AddScoped<ICommandHandler, TimeCommandHandler>();
        services.AddScoped<ICommandHandler, OnlineCommandHandler>();
        services.AddScoped<ICommandHandler, OfflineCommandHandler>();
        services.AddScoped<ICommandHandler, TeamCommandHandler>();
        services.AddScoped<ICommandHandler, SteamIdCommandHandler>();
        services.AddScoped<ICommandHandler, AliveCommandHandler>();
        services.AddScoped<ICommandHandler, AfkCommandHandler>();
        services.AddScoped<ICommandHandler, ProxCommandHandler>();
        services.AddScoped<ICommandHandler, CargoCommandHandler>();
        services.AddScoped<ICommandHandler, HeliCommandHandler>();
        services.AddScoped<ICommandHandler, ChinookCommandHandler>();
        services.AddScoped<ICommandHandler, EventsCommandHandler>();
        services.AddScoped<ICommandHandler, SmallCommandHandler>();
        services.AddScoped<ICommandHandler, LargeCommandHandler>();
        services.AddScoped<ICommandHandler, ItemCommandHandler>();
        services.AddScoped<ICommandHandler, RecycleCommandHandler>();
        services.AddScoped<ICommandHandler, CraftCommandHandler>();
        services.AddScoped<ICommandHandler, ResearchCommandHandler>();
        services.AddScoped<ICommandHandler, DecayCommandHandler>();
        services.AddScoped<ICommandHandler, UpkeepCommandHandler>();
        services.AddScoped<ICommandHandler, DurabilityCommandHandler>();
        services.AddScoped<ICommandHandler, SmeltCommandHandler>();
        services.AddScoped<ICommandHandler, CctvCommandHandler>();

        services.AddScoped<CommandDispatcher>();
        services.AddHostedService<CommandsHostedService>();

        // Discord surfaces (3c): help renderer, leader service, and the interaction modules.
        services.AddScoped<HelpEmbedRenderer>();
        services.AddScoped<LeaderService>();
        services.AddScoped<ServerResolver>();
        services.AddScoped<ServerQueryService>();
        services.AddSingleton(new InteractionModuleAssembly(typeof(CommandServiceCollectionExtensions).Assembly));

        return services;
    }
}
