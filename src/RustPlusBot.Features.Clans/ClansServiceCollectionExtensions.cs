using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.Clans.Hosting;
using RustPlusBot.Features.Clans.Messages;
using RustPlusBot.Features.Clans.Names;
using RustPlusBot.Features.Clans.Posting;
using RustPlusBot.Features.Clans.State;
using RustPlusBot.Features.Clans.Writing;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Clans;

/// <summary>DI registration for the clan feature (state, #claninfo embeds and feed, Set MOTD).</summary>
public static class ClansServiceCollectionExtensions
{
    /// <summary>
    /// Registers the clan state service, the workspace clan capability, the #claninfo feed poster
    /// and renderers, the MOTD writer, and the hosted consumer loop.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddClans(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRustPlusBotLocalization();

        services.AddSingleton<IClanFeedPoster, DiscordClanFeedPoster>();
        services.AddSingleton<ClanChangeRenderer>();
        services.AddSingleton<ClanStateService>();

        // One instance, two roles: the registry indexes capability providers by name (a duplicate
        // "clan" registration would throw at construction), and the state service invalidates the
        // very same cache before a transition reconcile.
        services.AddSingleton<ClanCapabilityProvider>();
        services.AddSingleton<IWorkspaceCapabilityProvider>(sp => sp.GetRequiredService<ClanCapabilityProvider>());

        // Scoped: these reach the DbContext through IClanStore.
        services.AddScoped<IClanNameResolver, ClanNameResolver>();
        services.AddScoped<IClanMotdWriter, ClanMotdWriter>();
        services.AddScoped<IMessageRenderer, ClanOverviewMessageRenderer>();
        services.AddScoped<IMessageRenderer, ClanRosterMessageRenderer>();
        services.AddScoped<IMessageRenderer, ClanInvitesMessageRenderer>();

        services.AddSingleton(new InteractionModuleAssembly(typeof(ClansServiceCollectionExtensions).Assembly));
        services.AddHostedService<ClansHostedService>();

        return services;
    }
}
