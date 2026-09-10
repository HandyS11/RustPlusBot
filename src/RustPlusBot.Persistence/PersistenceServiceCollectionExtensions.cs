using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Persistord.Core.Interception;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Persistence.Alarms;
using RustPlusBot.Persistence.Clans;
using RustPlusBot.Persistence.Commands;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Credentials;
using RustPlusBot.Persistence.Maintenance;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.StorageMonitors;
using RustPlusBot.Persistence.Switches;
using RustPlusBot.Persistence.Vending;
using RustPlusBot.Persistence.Wipes;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Persistence;

/// <summary>DI registration for the persistence layer.</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Registers the BotDbContext factory and the guild-scoped services over SQLite.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddBotPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The clock the TimestampInterceptor stamps from. TryAdd so a host (or a test) that registered
        // its own TimeProvider keeps it.
        services.TryAddSingleton(TimeProvider.System);

        // Every ICreatedAt/IUpdatedAt row is stamped by the interceptor rather than by each store, so
        // a store that forgets to touch a timestamp can no longer write a stale one.
        services.AddDbContextFactory<BotDbContext>((sp, options) => options
            .UseSqlite(connectionString)
            .AddInterceptors(new TimestampInterceptor(sp.GetRequiredService<TimeProvider>())));

        // AddDbContextFactory registers only the singleton factory, not a scoped context.
        // Register a scoped BotDbContext sourced from the factory so the scoped services below
        // (which take BotDbContext directly) resolve, while the factory remains available for
        // the startup migration in the Host.
        services.AddScoped<BotDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<BotDbContext>>().CreateDbContext());

        services.AddScoped<IServerService, ServerService>();
        services.AddScoped<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
        services.AddScoped<ICredentialStore, CredentialStore>();
        services.AddScoped<IFcmRegistrationStore, FcmRegistrationStore>();
        services.AddScoped<IWorkspaceStore, WorkspaceStore>();
        services.AddScoped<IConnectionStore, ConnectionStore>();
        services.AddScoped<ISwitchStore, SwitchStore>();
        services.AddScoped<IAlarmStore, AlarmStore>();
        services.AddScoped<IStorageMonitorStore, StorageMonitorStore>();
        services.AddScoped<IMuteStore, MuteStore>();
        services.AddScoped<IMapSettingsStore, MapSettingsStore>();
        services.AddScoped<IWipeBaselineStore, WipeBaselineStore>();
        services.AddScoped<IClanStore, ClanStore>();
        services.AddScoped<IVendingStore, VendingStore>();

        return services;
    }
}
