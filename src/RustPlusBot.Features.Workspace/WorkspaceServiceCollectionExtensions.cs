using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Specs;
using RustPlusBot.Features.Workspace.Teardown;

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
        services.AddSingleton(LocalizationCatalog.Default);
        services.AddSingleton<ILocalizer, Localizer>();

        // Gateway + per-guild lock.
        services.AddSingleton<IWorkspaceGateway, DiscordWorkspaceGateway>();
        services.AddSingleton<IProvisioningLock, ProvisioningLock>();

        // Renderers (scoped: some query the DbContext via IServerService).
        services.AddScoped<IMessageRenderer, InformationMessageRenderer>();
        services.AddScoped<IMessageRenderer, SetupMessageRenderer>();
        services.AddScoped<IMessageRenderer, SettingsMessageRenderer>();
        services.AddScoped<IMessageRenderer, ServerInfoMessageRenderer>();

        // Reconciler + teardown (scoped).
        services.AddScoped<IWorkspaceReconciler, WorkspaceReconciler>();
        services.AddScoped<IWorkspaceTeardownService, WorkspaceTeardownService>();

        // Options (Host binds the "Workspace" section; default = danger commands off).
        services.AddOptions<WorkspaceOptions>();

        // Contribute this assembly's interaction modules to the Discord layer.
        services.AddSingleton(new InteractionModuleAssembly(typeof(WorkspaceServiceCollectionExtensions).Assembly));

        // NOTE: WorkspaceHostedService (Hosting namespace) is registered via AddHostedService in Task 24.

        return services;
    }
}
