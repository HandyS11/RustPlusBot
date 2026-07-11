namespace RustPlusBot.Features.Map.Rendering;

/// <summary>Which overlay layers the renderer should draw.</summary>
/// <param name="Grid">Draw the map grid lines.</param>
/// <param name="Markers">Draw live cargo/heli/chinook markers.</param>
/// <param name="Monuments">Draw monument icons.</param>
/// <param name="Vendor">Draw the travelling-vendor marker.</param>
/// <param name="Players">Draw teammate position markers.</param>
/// <param name="Rigs">Style oil rigs by activation state.</param>
/// <param name="Tunnels">Draw train-tunnel entrance icons.</param>
public sealed record MapLayerSet(
    bool Grid,
    bool Markers,
    bool Monuments,
    bool Vendor,
    bool Players,
    bool Rigs,
    bool Tunnels = false)
{
    /// <summary>All layers enabled — the defaults-on render.</summary>
    public static MapLayerSet AllOn { get; } = new(true, true, true, true, true, true, true);
}
