using SkiaSharp;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>Small SkiaSharp helpers shared by the renderer and the icon sources.</summary>
internal static class Raster
{
    /// <summary>Smooth sampling for scaled or rotated draws; mipmaps keep large downscales clean.</summary>
    internal static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>Decodes encoded image bytes (PNG/JPEG/…) into a raster image.</summary>
    /// <param name="stream">The encoded image.</param>
    /// <returns>The decoded raster image; caller owns disposal.</returns>
    /// <exception cref="InvalidOperationException">The bytes are not a decodable image.</exception>
    internal static SKImage Decode(Stream stream)
    {
        using var bitmap = SKBitmap.Decode(stream)
                           ?? throw new InvalidOperationException("The image data could not be decoded.");
        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>Draws onto a fresh transparent canvas and returns the result as an immutable raster image.</summary>
    /// <param name="width">The canvas width in pixels.</param>
    /// <param name="height">The canvas height in pixels.</param>
    /// <param name="draw">Draws the content.</param>
    /// <returns>The drawn raster image; caller owns disposal.</returns>
    internal static SKImage Draw(int width, int height, Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        draw(surface.Canvas);
        return surface.Snapshot();
    }

    /// <summary>Encodes an image as PNG.</summary>
    /// <param name="image">The image to encode.</param>
    /// <returns>The PNG bytes.</returns>
    internal static byte[] EncodePng(SKImage image)
    {
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
