namespace RustPlusBot.Features.Map.Composing;

/// <summary>A fetched base-map image plus the projection metadata of that specific image.</summary>
/// <param name="Bytes">The encoded image bytes (JPEG or PNG).</param>
/// <param name="PixelWidth">Image width in pixels.</param>
/// <param name="PixelHeight">Image height in pixels.</param>
/// <param name="OceanMarginPx">Ocean border baked into this image, in pixels per side.</param>
public sealed record BaseMapImage(byte[] Bytes, int PixelWidth, int PixelHeight, int OceanMarginPx);
