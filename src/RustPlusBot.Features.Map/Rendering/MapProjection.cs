namespace RustPlusBot.Features.Map.Rendering;

/// <summary>
/// Projects world coordinates onto the rendered output image.
/// The canonical Rust+ transform: the playable world spans
/// [OceanMarginPx, ImageWidth - OceanMarginPx] on the base image, then the base image is
/// stretched to the square output. Matches the official app and rustplusplus.
/// </summary>
/// <param name="WorldSize">Size of the playable world, in game units.</param>
/// <param name="ImageWidth">Base map image width, in pixels.</param>
/// <param name="ImageHeight">Base map image height, in pixels.</param>
/// <param name="OceanMarginPx">Ocean border baked into the base image, in pixels per side.</param>
/// <param name="OutputSize">Output image edge length, in pixels.</param>
public sealed record MapProjection(uint WorldSize, int ImageWidth, int ImageHeight, int OceanMarginPx, int OutputSize)
{
    /// <summary>Converts a world (x, y) to an output pixel (x, y); origin top-left, Y down.</summary>
    /// <param name="worldX">World X (west→east).</param>
    /// <param name="worldY">World Y (south→north).</param>
    /// <returns>The output-space pixel coordinate.</returns>
    public (float X, float Y) ToPixel(float worldX, float worldY)
    {
        var imgX = (worldX * ((ImageWidth - (2f * OceanMarginPx)) / WorldSize)) + OceanMarginPx;
        var imgY = ImageHeight - ((worldY * ((ImageHeight - (2f * OceanMarginPx)) / WorldSize)) + OceanMarginPx);
        return (imgX * ((float)OutputSize / ImageWidth), imgY * ((float)OutputSize / ImageHeight));
    }
}
