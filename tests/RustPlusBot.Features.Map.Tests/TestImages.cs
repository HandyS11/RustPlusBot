using SkiaSharp;

namespace RustPlusBot.Features.Map.Tests;

/// <summary>Builds and decodes the images the map tests feed to and read back from the renderer.</summary>
internal static class TestImages
{
    /// <summary>A solid-color square JPEG, so the renderer has a real base image to decode.</summary>
    /// <param name="size">The edge length in pixels.</param>
    /// <param name="color">The fill color.</param>
    /// <returns>The JPEG bytes.</returns>
    public static byte[] SolidJpeg(int size, SKColor color)
    {
        using var bitmap = new SKBitmap(size, size);
        bitmap.Erase(color);
        using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        return jpeg.ToArray();
    }

    /// <summary>The 64x64 solid-green JPEG most tests use as a base map.</summary>
    /// <returns>The JPEG bytes.</returns>
    public static byte[] GreenJpeg() => SolidJpeg(64, new SKColor(0, 128, 0));

    /// <summary>Decodes encoded image bytes, failing the test when they are not a valid image.</summary>
    /// <param name="bytes">The encoded image.</param>
    /// <returns>The decoded bitmap.</returns>
    /// <exception cref="InvalidOperationException">The bytes are not a decodable image.</exception>
    public static SKBitmap Decode(byte[] bytes) =>
        SKBitmap.Decode(bytes) ?? throw new InvalidOperationException("Not a decodable image.");
}
