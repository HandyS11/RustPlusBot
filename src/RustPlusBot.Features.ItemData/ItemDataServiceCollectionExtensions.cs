using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.ItemData;

/// <summary>DI registration for the item database.</summary>
public static class ItemDataServiceCollectionExtensions
{
    /// <summary>Registers the item database and name resolver as singletons.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddItemData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IItemDatabase, EmbeddedItemDatabase>();
        services.AddSingleton<IItemNameResolver, ItemDatabaseNameResolver>();
        return services;
    }
}
