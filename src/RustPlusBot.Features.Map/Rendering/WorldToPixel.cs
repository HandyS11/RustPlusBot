using RustPlusBot.Abstractions.Connections;

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

        // The full tile adds the ocean margin on each side, so each axis spans
        // [-margin, dimension+margin]. Each axis is scaled by its own dimension so the projection
        // is correct even if a server ever reports a non-square map (Rust maps are square today).
        var margin = dims.OceanMargin;
        var perUnitX = outputSize / (dims.Width + (2f * margin));
        var perUnitY = outputSize / (dims.Height + (2f * margin));

        var px = (worldX + margin) * perUnitX;
        // Flip Y: world south (0) is the visual bottom (image y = outputSize), world north is the top.
        var py = outputSize - ((worldY + margin) * perUnitY);
        return (px, py);
    }
}
