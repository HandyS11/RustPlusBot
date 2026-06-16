using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.Connections.Hosting;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Connections.Removal;
using RustPlusBot.Features.Connections.Supervisor;

namespace RustPlusBot.Features.Connections;

/// <summary>DI registration for the live-connection feature.</summary>
public static class ConnectionServiceCollectionExtensions
{
    /// <summary>Registers the socket source, supervisor, module seam, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddConnections(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRustSocketSource, RustPlusSocketSource>();
        services.AddSingleton<ConnectionSupervisor>();
        services.AddSingleton<IConnectionSupervisor>(sp => sp.GetRequiredService<ConnectionSupervisor>());
        services.AddSingleton<ITeamChatSender>(sp => sp.GetRequiredService<ConnectionSupervisor>());
        services.AddSingleton<IRustServerQuery>(sp => sp.GetRequiredService<ConnectionSupervisor>());
        services.AddScoped<IServerRemovalService, ServerRemovalService>();

        // Contribute this assembly's interaction modules to the Discord layer.
        services.AddSingleton(new InteractionModuleAssembly(typeof(ConnectionServiceCollectionExtensions).Assembly));

        services.AddHostedService<ConnectionHostedService>();

        return services;
    }
}
