using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Assets;
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

    /// <summary>Composes the map PNG for a server using its saved layer toggles, or null when no base map
    /// is available yet.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The rendered composition, or null when no base map is available yet.</returns>
    public async Task<MapComposition?> ComposeAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
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
            settings.Vendor, settings.Players, settings.Rigs, settings.Tunnels);
        return await ComposeWithLayersAsync(guildId, serverId, layers, settings.GridStyle, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MapComposition?> ComposeWithLayersAsync(
        ulong guildId,
        Guid serverId,
        MapLayerSet layers,
        MapGridStyle gridStyle,
        CancellationToken cancellationToken)
    {
        var baseImage = await cache.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (baseImage is null)
        {
            return null;
        }

        // Dimensions come from the map itself (not from a marker), so the grid renders even when no
        // markers are present — e.g. on a freshly-connected or low-activity server.
        var dims = await query.GetMapDimensionsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (dims is null || dims.WorldSize == 0)
        {
            // Dimensions unavailable: render the base tile only (every overlay needs world→pixel).
            return new MapComposition(
                renderer.Render(new MapRenderRequest
                {
                    BaseJpeg = baseImage.Bytes,
                    Projection = new MapProjection(0, 1, 1, 0, MapRenderer.OutputSize),
                    Markers = [],
                    Monuments = [],
                    Players = [],
                    Rigs = [],
                    Layers = new MapLayerSet(Grid: false, Markers: false, Monuments: false, Vendor: false,
                        Players: false, Rigs: false, Tunnels: false),
                }),
                Legend: null);
        }

        var projection = new MapProjection(dims.WorldSize, baseImage.PixelWidth, baseImage.PixelHeight,
            baseImage.OceanMarginPx, MapRenderer.OutputSize);

        var markers = GatherMarkers(guildId, serverId, projection, layers);

        // Monuments feed the monuments, rig-styling, and tunnels layers; fetch them once when any is on.
        IReadOnlyList<MonumentSnapshot> serverMonuments = [];
        if (layers.Monuments || layers.Rigs || layers.Tunnels)
        {
            serverMonuments = await query.GetMonumentsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        }

        var monuments = GatherMonuments(serverMonuments, projection, layers);
        var tunnels = GatherTunnels(serverMonuments, projection, layers);
        var (players, legend) = await GatherPlayersAsync(guildId, serverId, projection, layers, cancellationToken)
            .ConfigureAwait(false);
        var rigPlacements = GatherRigs(guildId, serverId, serverMonuments, projection, layers);

        var png = renderer.Render(new MapRenderRequest
        {
            BaseJpeg = baseImage.Bytes,
            Projection = projection,
            Markers = markers,
            Monuments = monuments,
            Players = players,
            Rigs = rigPlacements,
            Layers = layers,
            GridStyle = gridStyle,
            Tunnels = tunnels,
        });
        return new MapComposition(png, legend);
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
                if (TunnelTokens.All.Contains(mon.Token))
                {
                    continue; // routed to the Tunnels layer instead
                }

                var (px, py) = projection.ToPixel(mon.X, mon.Y);
                monuments.Add(new MonumentPlacement(mon.Token, px, py));
            }
        }

        return monuments;
    }

    private static List<MonumentPlacement> GatherTunnels(
        IReadOnlyList<MonumentSnapshot> serverMonuments,
        MapProjection projection,
        MapLayerSet layers)
    {
        var tunnels = new List<MonumentPlacement>();
        if (layers.Tunnels)
        {
            foreach (var mon in serverMonuments.Where(m => TunnelTokens.All.Contains(m.Token)))
            {
                var (px, py) = projection.ToPixel(mon.X, mon.Y);
                tunnels.Add(new MonumentPlacement(mon.Token, px, py));
            }
        }

        return tunnels;
    }

    private async Task<(List<PlayerPlacement> Players, MapLegend? Legend)> GatherPlayersAsync(
        ulong guildId,
        Guid serverId,
        MapProjection projection,
        MapLayerSet layers,
        CancellationToken cancellationToken)
    {
        if (!layers.Players)
        {
            return ([], null);
        }

        var team = await query.GetTeamInfoAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        // Stable color per player: order by SteamId, index into the palette. SteamId never changes,
        // so a player keeps their color across refreshes regardless of online/offline ordering. The
        // legend is built from the same index so a cross color and its legend row never drift.
        var ordered = (team?.Members ?? []).OrderBy(m => m.SteamId).ToList();
        if (ordered.Count == 0)
        {
            return ([], null);
        }

        var players = new List<PlayerPlacement>(ordered.Count);
        var legend = new List<MapLegendEntry>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var member = ordered[i];
            var color = PlayerPalette.For(i);
            var (px, py) = projection.ToPixel(member.X, member.Y);
            players.Add(new PlayerPlacement(member.Name, px, py, member.IsAlive, member.IsOnline, color.Rgba));
            legend.Add(new MapLegendEntry(color.Emoji, member.Name, StatusText(member)));
        }

        return (players, new MapLegend(legend));
    }

    private static string StatusText(TeamMemberSnapshot member)
    {
        var presence = member.IsOnline ? "online" : "offline";
        return member.IsAlive ? presence : presence + ", dead";
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
                    "oil_rig_small" => RigKind.Small,
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
