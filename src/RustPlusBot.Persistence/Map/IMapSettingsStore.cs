using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Persistence.Map;

/// <summary>Reads/writes per-(guild, server) map layer settings.</summary>
public interface IMapSettingsStore
{
    /// <summary>Gets the layer toggles, returning all-on defaults when no row exists.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved layer toggles.</returns>
    Task<MapLayerSettings> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Sets one layer's enabled state, creating the row (all-on) if needed.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="layer">Which layer to change.</param>
    /// <param name="enabled">The new enabled state.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when persisted.</returns>
    Task SetLayerAsync(ulong guildId,
        Guid serverId,
        MapLayer layer,
        bool enabled,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the grid style, creating the row (all-on layers) if needed.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="style">The grid convention to use.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when persisted.</returns>
    Task SetGridStyleAsync(ulong guildId,
        Guid serverId,
        MapGridStyle style,
        CancellationToken cancellationToken = default);
}
