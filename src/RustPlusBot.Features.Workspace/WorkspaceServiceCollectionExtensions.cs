using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Specs;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Workspace;

/// <summary>DI registration for the workspace provisioning feature.</summary>
public static class WorkspaceServiceCollectionExtensions
{
    /// <summary>Registers the registry, gateway, reconciler, renderers, teardown, and module seam.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddWorkspace(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Spec registry (stateless singletons).
        services.AddSingleton<IChannelSpecProvider, GlobalWorkspaceSpecProvider>();
        services.AddSingleton<IMessageSpecProvider, GlobalWorkspaceSpecProvider>();
        services.AddSingleton<IChannelSpecProvider, ServerWorkspaceSpecProvider>();
        services.AddSingleton<IMessageSpecProvider, ServerWorkspaceSpecProvider>();
        services.AddSingleton<IWorkspaceRegistry, WorkspaceRegistry>();

        // Localization.
        services.AddRustPlusBotLocalization();

        // Gateway + per-guild lock.
        services.AddSingleton<IWorkspaceGateway, DiscordWorkspaceGateway>();
        services.AddSingleton<IProvisioningLock, ProvisioningLock>();

        // Renderers (scoped: some query the DbContext via IServerService).
        services.AddScoped<IMessageRenderer, InformationMessageRenderer>();
        services.AddScoped<IMessageRenderer, SetupMessageRenderer>();
        services.AddScoped<IMessageRenderer, SettingsMessageRenderer>();
        // ServerInfoMessageRenderer requires IRustServerQuery, which is registered by AddConnections —
        // the host must compose both AddWorkspace and AddConnections.
        services.AddScoped<IMessageRenderer, ServerInfoMessageRenderer>();
        // ServerInfoMapMessageRenderer takes IInfoMapReadModel as an OPTIONAL dependency: it only resolves
        // when Features.Map registered it (RustMaps API key present); otherwise the renderer returns an
        // empty payload and the reconciler never posts a map message.
        services.AddScoped<IMessageRenderer, ServerInfoMapMessageRenderer>();
        services.AddScoped<IMessageRenderer, MapControlMessageRenderer>();

        // Reconciler + teardown (scoped). Register the teardown service once and expose both interfaces
        // off the same scoped instance, so resolving either does not create a second instance.
        services.AddScoped<WorkspaceBackends>();
        services.AddScoped<IWorkspaceReconciler, WorkspaceReconciler>();
        services.AddScoped<WorkspaceTeardownService>();
        services.AddScoped<IWorkspaceTeardownService>(sp => sp.GetRequiredService<WorkspaceTeardownService>());
        services.AddScoped<IServerWorkspaceRemover>(sp => sp.GetRequiredService<WorkspaceTeardownService>());
        services.AddScoped<IGuildPurgeService, GuildPurgeService>();

        // Options (Host binds the "Workspace" section; default = danger commands off).
        services.AddOptions<WorkspaceOptions>();

        // Contribute this assembly's interaction modules to the Discord layer.
        services.AddSingleton(new InteractionModuleAssembly(typeof(WorkspaceServiceCollectionExtensions).Assembly));

        // Channel locators (singleton with TTL cache; IClock + IServiceScopeFactory provided by the host).
        services.AddSingleton<ITeamChatChannelLocator, TeamChatChannelLocator>();
        services.AddSingleton<IEventChannelLocator, EventChannelLocator>();
        services.AddSingleton<IMapChannelLocator, MapChannelLocator>();
        services.AddSingleton<ISwitchChannelLocator, SwitchChannelLocator>();
        services.AddSingleton<IAlarmChannelLocator, AlarmChannelLocator>();
        services.AddSingleton<IStorageMonitorChannelLocator, StorageMonitorChannelLocator>();

        services.AddHostedService<Hosting.WorkspaceHostedService>();

        return services;
    }
}
