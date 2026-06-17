using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map.Composing;

/// <summary>Gathers the cached base map + live markers and renders the map PNG.</summary>
/// <param name="cache">The base-map cache.</param>
/// <param name="events">Live marker state.</param>
/// <param name="renderer">The image renderer.</param>
public sealed class MapComposer(BaseMapCache cache, IEventState events, MapRenderer renderer)
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

        var active = DrawnKinds
            .SelectMany(kind => events.GetActiveMarkers(guildId, serverId, kind))
            .ToList();

        var dims = active.FirstOrDefault(m => m.Dimensions is not null)?.Dimensions;
        if (dims is null)
        {
            // No dimensions known yet: render the base tile only (no grid/markers need world→pixel).
            return renderer.Render(baseImage, new MapDimensions(0, 0, 0), markers: [],
                new MapLayerSet(Grid: false, Markers: false, Monuments: false, Vendor: false, Rigs: false));
        }

        var placements = active.ConvertAll(m =>
        {
            var (px, py) = WorldToPixel.ToPixel(m.X, m.Y, dims, MapRenderer.OutputSize);
            return new MarkerPlacement(m.Kind, px, py);
        });

        return renderer.Render(baseImage, dims, placements, MapLayerSet.Default2b);
    }
}
