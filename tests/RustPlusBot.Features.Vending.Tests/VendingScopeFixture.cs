using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Vending;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>
/// Builds the scoped-store provider the vending relay and wipe purger resolve from. Both are
/// singletons that open a scope per event, so every test of either needs the same three scoped
/// registrations behind an <see cref="IServiceScopeFactory"/>.
/// </summary>
internal static class VendingScopeFixture
{
    /// <summary>Creates a provider whose scopes yield substituted, initially-empty vending stores.</summary>
    /// <returns>The provider plus the two doubles tests assert against.</returns>
    public static (ServiceProvider Provider, IVendingStore Store, IWorkspaceStore Workspace) Create()
    {
        var store = Substitute.For<IVendingStore>();
        store.ListGridsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([]);
        store.ListListingsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([]);
        store.ListNotificationsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([]);
        store.ListStockNotificationsAsync(default, Guid.Empty, default).ReturnsForAnyArgs([]);

        var settings = Substitute.For<IMapSettingsStore>();
        settings.GetAsync(default, Guid.Empty, default).ReturnsForAnyArgs(MapLayerSettings.AllOn);

        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(default, default).ReturnsForAnyArgs("en");

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped(_ => settings);
        services.AddScoped(_ => workspace);
        return (services.BuildServiceProvider(), store, workspace);
    }
}
