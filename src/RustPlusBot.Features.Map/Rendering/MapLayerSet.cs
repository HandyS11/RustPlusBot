namespace RustPlusBot.Features.Map.Rendering;

/// <summary>Which overlay layers the renderer should draw. 2b uses <see cref="Default2b"/>; 2b-ii feeds this from per-server settings.</summary>
/// <param name="Grid">Draw the A0–Z grid lines and labels.</param>
/// <param name="Markers">Draw live cargo/heli/chinook markers.</param>
/// <param name="Monuments">Draw monument icons (2b-ii).</param>
/// <param name="Vendor">Draw the travelling-vendor marker (2b-ii).</param>
/// <param name="Rigs">Style oil rigs by activation state (2b-ii).</param>
public sealed record MapLayerSet(bool Grid, bool Markers, bool Monuments, bool Vendor, bool Rigs)
{
    /// <summary>The fixed layer set for subsystem 2b: grid + live markers on, the rest off.</summary>
    public static MapLayerSet Default2b { get; } =
        new(Grid: true, Markers: true, Monuments: false, Vendor: false, Rigs: false);
}
