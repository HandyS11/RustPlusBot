using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RustMapsApi.V4.Assets;
using RustMapsApi.V4.Models;
using RustPlusBot.Abstractions.Events;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using Svg.Skia;

namespace RustPlusBot.Features.Map.Assets;

/// <summary>
/// Serves monument icons from the RustMaps asset package: resolves a Rust+ token to a
/// <see cref="MonumentType"/>, rasterizes the SVG at the requested size, and caches the result
/// (including null misses). Rasterization runs exactly once per (type, size), even when concurrent
/// first requests race. Cached images are immutable inputs the renderer draws from; never mutate them.
/// Register as a singleton.
/// </summary>
/// <param name="assets">The RustMaps monument asset source.</param>
/// <param name="logger">Logs tokens that resolve to no icon, once per token per process.</param>
public sealed partial class MonumentIconSource(IMonumentAssetSource assets, ILogger<MonumentIconSource> logger)
{
    private readonly ConcurrentDictionary<(MonumentType Type, int Size), Lazy<Image<Rgba32>?>> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.Ordinal);

    /// <summary>Gets the icon for a monument token scaled to a square box, or null when no icon exists.</summary>
    /// <param name="token">The Rust+ monument protobuf token (or prefab name).</param>
    /// <param name="size">The box edge length in pixels.</param>
    /// <returns>The cached icon, or null (reported once per token).</returns>
    public Image<Rgba32>? Monument(string token, int size)
    {
        var type = MonumentTokenMap.TypeFor(token);
        if (type is null)
        {
            if (!MonumentTokenMap.IsIgnored(token))
            {
                ReportOnce(token, type: null);
            }

            return null;
        }

        return For(type.Value, token, size);
    }

    /// <summary>Gets the rig icon scaled to a square box, or null for an unknown kind.</summary>
    /// <param name="kind">Which rig.</param>
    /// <param name="size">The box edge length in pixels.</param>
    /// <returns>The cached rig icon, or null.</returns>
    public Image<Rgba32>? Rig(RigKind kind, int size) => kind switch
    {
        RigKind.Small => For(MonumentType.OilrigSmall, "oil_rig_small", size),
        RigKind.Large => For(MonumentType.OilrigLarge, "large_oil_rig", size),
        _ => null,
    };

    private Image<Rgba32>? For(MonumentType type, string token, int size)
    {
        // GetOrAdd's value factory may run more than once under a first-request race; the Lazy wrapper
        // (ExecutionAndPublication) guarantees Rasterize itself runs exactly once per key, so no losing
        // image is leaked and the rasterization-failure Warning fires at most once per (type, size).
        var image = _cache
            .GetOrAdd((type, size), key => new Lazy<Image<Rgba32>?>(() => Rasterize(key.Type, key.Size)))
            .Value;
        if (image is null)
        {
            ReportOnce(token, type);
        }

        return image;
    }

    private void ReportOnce(string token, MonumentType? type)
    {
        if (_reported.TryAdd(token, 0))
        {
            LogNoIcon(token, type);
        }
    }

    private Image<Rgba32>? Rasterize(MonumentType type, int size)
    {
        if (!assets.TryGetAsset(type, out var asset))
        {
            return null;
        }

        try
        {
            using var svg = new SKSvg();
            using (var stream = asset.OpenStream())
            {
                if (svg.Load(stream) is null)
                {
                    return null;
                }
            }

            var bounds = svg.Picture!.CullRect;
            using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(size / bounds.Width, size / bounds.Height);
                canvas.Translate(-bounds.Left, -bounds.Top);
                canvas.DrawPicture(svg.Picture);
            }

            using var skImage = SKImage.FromBitmap(bitmap);
            using var png = skImage.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = png.AsStream();
            return Image.Load<Rgba32>(ms);
        }
#pragma warning disable CA1031 // Broad catch: a bad SVG must degrade to "no icon", never break a render.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRasterFailed(type, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "No monument icon for token \"{Token}\" (mapped type: {Type}); skipped.")]
    private partial void LogNoIcon(string token, MonumentType? type);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Rasterizing the {Type} icon failed; the monument renders without an icon.")]
    private partial void LogRasterFailed(MonumentType type, Exception ex);
}
