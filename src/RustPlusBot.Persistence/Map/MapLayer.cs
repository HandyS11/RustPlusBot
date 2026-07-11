using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Persistence.Map;

/// <summary>A single toggleable map render layer.</summary>
public enum MapLayer
{
    /// <summary>The grid lines layer.</summary>
    Grid = 0,

    /// <summary>The live cargo/heli/chinook markers layer.</summary>
    Markers = 1,

    /// <summary>The monument icons layer.</summary>
    Monuments = 2,

    /// <summary>The travelling-vendor layer.</summary>
    Vendor = 3,

    /// <summary>The teammate-positions layer.</summary>
    Players = 4,

    /// <summary>The oil-rig activation-styling layer.</summary>
    Rigs = 5,

    /// <summary>The train-tunnel entrance layer.</summary>
    Tunnels = 6,
}

/// <summary>The resolved per-server layer toggles (all-on when no row is stored).</summary>
/// <param name="Grid">Grid lines.</param>
/// <param name="Markers">Live cargo/heli/chinook markers.</param>
/// <param name="Monuments">Monument icons.</param>
/// <param name="Vendor">Travelling vendor.</param>
/// <param name="Players">Teammate positions.</param>
/// <param name="Rigs">Oil-rig activation styling.</param>
/// <param name="Tunnels">Train-tunnel entrance icons.</param>
/// <param name="GridStyle">Which grid convention the render and event grid references use.</param>
public sealed record MapLayerSettings(
    bool Grid,
    bool Markers,
    bool Monuments,
    bool Vendor,
    bool Players,
    bool Rigs,
    bool Tunnels = true,
    MapGridStyle GridStyle = MapGridStyle.InGame)
{
    /// <summary>All layers enabled — the default when no settings row exists.</summary>
    public static MapLayerSettings AllOn { get; } = new(true, true, true, true, true, true);
}
