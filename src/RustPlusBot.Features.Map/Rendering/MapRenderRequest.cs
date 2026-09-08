using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>The base image, projection, and overlay data needed to render a map PNG.</summary>
internal sealed record MapRenderRequest
{
    /// <summary>The raw base-map JPEG bytes.</summary>
    public required byte[] BaseJpeg { get; init; }

    /// <summary>The world-to-pixel projection (world size, base image dims, ocean margin).</summary>
    public required MapProjection Projection { get; init; }

    /// <summary>Marker placements already projected to pixel coordinates.</summary>
    public required IReadOnlyList<MarkerPlacement> Markers { get; init; }

    /// <summary>Monument placements already projected to pixel coordinates.</summary>
    public required IReadOnlyList<MonumentPlacement> Monuments { get; init; }

    /// <summary>Player placements already projected to pixel coordinates.</summary>
    public required IReadOnlyList<PlayerPlacement> Players { get; init; }

    /// <summary>Oil-rig placements already projected to pixel coordinates.</summary>
    public required IReadOnlyList<RigPlacement> Rigs { get; init; }

    /// <summary>Which overlay layers to draw.</summary>
    public required MapLayerSet Layers { get; init; }

    /// <summary>Which grid convention to draw (in-game F1 map, or Rust+/RustMaps).</summary>
    public MapGridStyle GridStyle { get; init; } = MapGridStyle.InGame;

    /// <summary>Train-tunnel placements already projected to pixel coordinates.</summary>
    public IReadOnlyList<MonumentPlacement>? Tunnels { get; init; }
}
