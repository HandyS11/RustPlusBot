using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RustPlusBot.Localization;

/// <summary>DI registration for the shared localizer.</summary>
public static class LocalizationServiceCollectionExtensions
{
    /// <summary>Registers the shared <see cref="ILocalizer"/> (idempotent).</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddRustPlusBotLocalization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ILocalizer, ResxLocalizer>();
        return services;
    }
}
