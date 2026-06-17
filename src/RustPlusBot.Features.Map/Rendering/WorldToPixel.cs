using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>Maps world coordinates to pixel coordinates on a square rendered map tile.</summary>
public static class WorldToPixel
{
    /// <summary>Converts a world (x, y) to a pixel (x, y) on a square output of the given edge length.</summary>
    /// <param name="worldX">World X (west→east), in [0, Width].</param>
    /// <param name="worldY">World Y (south→north), in [0, Height].</param>
    /// <param name="dims">The map dimensions (width/height in game units + ocean margin).</param>
    /// <param name="outputSize">The output image edge length in pixels.</param>
    /// <returns>The pixel coordinate (origin top-left, Y down).</returns>
    public static (float X, float Y) ToPixel(float worldX, float worldY, MapDimensions dims, int outputSize)
    {
        ArgumentNullException.ThrowIfNull(dims);

        // The full tile (with ocean margin on each side) spans [-margin, Width+margin].
        var margin = dims.OceanMargin;
        var span = dims.Width + (2f * margin);
        var perUnit = outputSize / span;

        var px = (worldX + margin) * perUnit;
        // Flip Y: world south (0) is the visual bottom (image y = outputSize), world north is the top.
        var py = outputSize - ((worldY + margin) * perUnit);
        return (px, py);
    }
}
