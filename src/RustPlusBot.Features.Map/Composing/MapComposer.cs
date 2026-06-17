using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map.Composing;

/// <summary>Gathers the cached base map + live markers and renders the map PNG.</summary>
/// <param name="cache">The base-map cache.</param>
/// <param name="events">Live marker state.</param>
/// <param name="query">Live query seam (supplies the static map dimensions).</param>
/// <param name="renderer">The image renderer.</param>
public sealed class MapComposer(BaseMapCache cache, IEventState events, IRustServerQuery query, MapRenderer renderer)
{
    private static readonly MarkerKind[] DrawnKinds =
    [
        MarkerKind.CargoShip, MarkerKind.PatrolHelicopter, MarkerKind.Chinook,
    ];

    /// <summary>Composes the map PNG for a server, or null when no base map is available yet.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>PNG bytes, or null.</returns>
    public async Task<byte[]?> ComposeAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var baseImage = await cache.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (baseImage is null)
        {
            return null;
        }

        // Dimensions come from the map itself (not from a marker), so the grid renders even when no
        // markers are present — e.g. on a freshly-connected or low-activity server.
        var dims = await query.GetMapDimensionsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (dims is null)
        {
            // Dimensions unavailable: render the base tile only (grid/markers both need world→pixel).
            return renderer.Render(baseImage, new MapDimensions(0, 0, 0), markers: [],
                new MapLayerSet(Grid: false, Markers: false, Monuments: false, Vendor: false, Rigs: false));
        }

        var placements = DrawnKinds
            .SelectMany(kind => events.GetActiveMarkers(guildId, serverId, kind))
            .Select(m =>
            {
                var (px, py) = WorldToPixel.ToPixel(m.X, m.Y, dims, MapRenderer.OutputSize);
                return new MarkerPlacement(m.Kind, px, py);
            })
            .ToList();

        return renderer.Render(baseImage, dims, placements, MapLayerSet.Default2b);
    }
}
