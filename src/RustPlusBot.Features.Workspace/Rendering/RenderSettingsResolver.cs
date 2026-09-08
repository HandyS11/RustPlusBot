using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Rendering;

/// <summary>
/// Resolves the guild's culture and a server's configured map grid style in one scope. Every relay that renders
/// map-relative content (events, player transitions) needs both, read together from the scoped stores.
/// </summary>
public static class RenderSettingsResolver
{
    /// <summary>Reads the guild's culture and the server's configured grid style.</summary>
    /// <param name="scopeFactory">Opens the scope for the scoped workspace and map-settings stores.</param>
    /// <param name="guildId">The Discord guild id.</param>
    /// <param name="serverId">The paired Rust+ server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The guild's culture and the server's configured grid style.</returns>
    public static async Task<(string Culture, MapGridStyle GridStyle)> GetAsync(
        IServiceScopeFactory scopeFactory,
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var culture = await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
            var mapSettings = scope.ServiceProvider.GetRequiredService<IMapSettingsStore>();
            var settings = await mapSettings.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            return (culture, settings.GridStyle);
        }
    }
}
