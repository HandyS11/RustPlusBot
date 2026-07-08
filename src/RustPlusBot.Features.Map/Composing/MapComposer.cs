using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Rendering;
using RustPlusBot.Persistence.Map;
using SixLabors.ImageSharp;

namespace RustPlusBot.Features.Map.Composing;

/// <summary>Gathers the cached base map, per-server settings, and live layer data, then renders the map PNG.</summary>
/// <param name="cache">The base-map cache.</param>
/// <param name="events">Live marker state.</param>
/// <param name="rigs">Inferred oil-rig state.</param>
/// <param name="query">Live query seam (dimensions, monuments, team).</param>
/// <param name="renderer">The image renderer.</param>
/// <param name="scopeFactory">Opens a scope to read the scoped settings store.</param>
public sealed class MapComposer(
    BaseMapCache cache,
    IEventState events,
    IRigState rigs,
    IRustServerQuery query,
    MapRenderer renderer,
    IServiceScopeFactory scopeFactory)
{
    private static readonly MarkerKind[] LiveMarkerKinds =
        [MarkerKind.CargoShip, MarkerKind.PatrolHelicopter, MarkerKind.Chinook];

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

        // The settings store is scoped (EF context); this composer is a singleton, so we open a scope per
        // call to resolve it (mirroring MapHostedService.OnConnectionStatusAsync) — avoids a captive dependency.
        MapLayerSettings settings;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IMapSettingsStore>();
            settings = await store.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        }

        var layers = new MapLayerSet(settings.Grid, settings.Markers, settings.Monuments,
            settings.Vendor, settings.Players, settings.Rigs);

        // Dimensions come from the map itself (not from a marker), so the grid renders even when no
        // markers are present — e.g. on a freshly-connected or low-activity server.
        var dims = await query.GetMapDimensionsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (dims is null || dims.WorldSize == 0)
        {
            // Dimensions unavailable: render the base tile only (every overlay needs world→pixel).
            return renderer.Render(baseImage, new MapProjection(0, 1, 1, 0, MapRenderer.OutputSize),
                markers: [], monuments: [], players: [], rigs: [],
                new MapLayerSet(Grid: false, Markers: false, Monuments: false, Vendor: false, Players: false,
                    Rigs: false));
        }

        // NOTE — Task 9 follow-up: image pixel dims come from MapDimensions for now, which is correct
        // for the Rust+ JPEG. The base-map source chain will replace this with the fetched image's own dims.
        var projection = new MapProjection(dims.WorldSize, (int)dims.Width, (int)dims.Height, dims.OceanMargin,
            MapRenderer.OutputSize);

        var markers = GatherMarkers(guildId, serverId, projection, layers);

        // Monuments feed both the monuments layer and the rig-styling layer; fetch them once when either is on.
        IReadOnlyList<MonumentSnapshot> serverMonuments = [];
        if (layers.Monuments || layers.Rigs)
        {
            serverMonuments = await query.GetMonumentsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        }

        var monuments = GatherMonuments(serverMonuments, projection, layers);
        var players = await GatherPlayersAsync(guildId, serverId, projection, layers, cancellationToken)
            .ConfigureAwait(false);
        var rigPlacements = GatherRigs(guildId, serverId, serverMonuments, projection, layers);

        return renderer.Render(baseImage, projection, markers, monuments, players, rigPlacements, layers);
    }

    private List<MarkerPlacement> GatherMarkers(
        ulong guildId,
        Guid serverId,
        MapProjection projection,
        MapLayerSet layers)
    {
        var markers = new List<MarkerPlacement>();
        if (layers.Markers)
        {
            foreach (var kind in LiveMarkerKinds)
            {
                foreach (var m in events.GetActiveMarkers(guildId, serverId, kind))
                {
                    var (px, py) = projection.ToPixel(m.X, m.Y);
                    var trail = ProjectTrail(m.History, projection);
                    markers.Add(new MarkerPlacement(kind, px, py, m.Rotation, trail));
                }
            }
        }

        if (layers.Vendor)
        {
            foreach (var m in events.GetActiveMarkers(guildId, serverId, MarkerKind.TravellingVendor))
            {
                var (px, py) = projection.ToPixel(m.X, m.Y);
                var trail = ProjectTrail(m.History, projection);
                markers.Add(new MarkerPlacement(MarkerKind.TravellingVendor, px, py, m.Rotation, trail));
            }
        }

        return markers;
    }

    private static List<PointF> ProjectTrail(IReadOnlyList<TrailPoint> history, MapProjection projection)
    {
        var trail = new List<PointF>(history.Count);
        foreach (var h in history)
        {
            var (tx, ty) = projection.ToPixel(h.X, h.Y);
            trail.Add(new PointF(tx, ty));
        }

        return trail;
    }

    private static List<MonumentPlacement> GatherMonuments(
        IReadOnlyList<MonumentSnapshot> serverMonuments,
        MapProjection projection,
        MapLayerSet layers)
    {
        var monuments = new List<MonumentPlacement>();
        if (layers.Monuments)
        {
            foreach (var mon in serverMonuments)
            {
                var (px, py) = projection.ToPixel(mon.X, mon.Y);
                monuments.Add(new MonumentPlacement(mon.Token, px, py));
            }
        }

        return monuments;
    }

    private async Task<List<PlayerPlacement>> GatherPlayersAsync(
        ulong guildId,
        Guid serverId,
        MapProjection projection,
        MapLayerSet layers,
        CancellationToken cancellationToken)
    {
        var players = new List<PlayerPlacement>();
        if (layers.Players)
        {
            var team = await query.GetTeamInfoAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            foreach (var member in team?.Members ?? [])
            {
                var (px, py) = projection.ToPixel(member.X, member.Y);
                players.Add(new PlayerPlacement(member.Name, px, py, member.IsAlive, member.IsOnline));
            }
        }

        return players;
    }

    private List<RigPlacement> GatherRigs(
        ulong guildId,
        Guid serverId,
        IReadOnlyList<MonumentSnapshot> serverMonuments,
        MapProjection projection,
        MapLayerSet layers)
    {
        var rigPlacements = new List<RigPlacement>();
        if (layers.Rigs)
        {
            // Reuse the monument positions for the two rig tokens; query rig state for activation styling.
            foreach (var mon in serverMonuments)
            {
                RigKind? kind = mon.Token switch
                {
                    "oilrig_1" => RigKind.Small,
                    "large_oil_rig" => RigKind.Large,
                    _ => null,
                };
                if (kind is { } k)
                {
                    var state = rigs.Get(guildId, serverId, k);
                    var (px, py) = projection.ToPixel(mon.X, mon.Y);
                    rigPlacements.Add(new RigPlacement(k, px, py, state.Status == RigStatus.Active));
                }
            }
        }

        return rigPlacements;
    }
}
