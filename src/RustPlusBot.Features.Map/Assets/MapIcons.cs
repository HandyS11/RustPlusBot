using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Connections;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RustPlusBot.Features.Map.Assets;

/// <summary>
/// Loads vendored icon assets (embedded PNGs) once and serves them by marker kind.
/// Cached images are immutable inputs the renderer draws from; never mutate them.
/// Monument and rig icons live in <see cref="MonumentIconSource"/>.
/// </summary>
/// <remarks>
/// <c>MapIcons.Marker</c> includes a <c>TravellingVendor</c> arm that returns the embedded vendor.png.
/// <see cref="Vendor"/> is retained as a bridge accessor for callers that reference it directly.
/// </remarks>
public static class MapIcons
{
    private const string ResourcePrefix = "RustPlusBot.Features.Map.Assets.icons.";

    private static readonly ConcurrentDictionary<string, Image<Rgba32>?> Cache = new(StringComparer.Ordinal);

    /// <summary>Gets the icon for a marker kind, or null when there is no icon for that kind.</summary>
    /// <param name="kind">The marker kind.</param>
    /// <returns>The cached icon image, or null.</returns>
    public static Image<Rgba32>? Marker(MarkerKind kind)
    {
        var key = KeyFor(kind);
        return key is null ? null : Native(key);
    }

    /// <summary>Gets the marker icon scaled to fit a square box, or null when the kind has no icon.</summary>
    /// <param name="kind">The marker kind.</param>
    /// <param name="size">The box edge length in pixels; the longest icon edge is scaled to it.</param>
    /// <returns>The cached scaled icon, or null.</returns>
    public static Image<Rgba32>? Marker(MarkerKind kind, int size) => Scaled(KeyFor(kind), size);

    /// <summary>Gets the travelling vendor icon.</summary>
    /// <returns>The cached vendor icon image, or null.</returns>
    public static Image<Rgba32>? Vendor() => Native("vendor");

    /// <summary>Gets the player position icon, or null when the asset is missing.</summary>
    /// <returns>The player icon, or null.</returns>
    public static Image<Rgba32>? Player() => Native("player");

    /// <summary>Gets the player icon scaled to fit a square box, or null when the asset is missing.</summary>
    /// <param name="size">The box edge length in pixels.</param>
    /// <returns>The cached scaled icon, or null.</returns>
    public static Image<Rgba32>? Player(int size) => Scaled("player", size);

    private static string? KeyFor(MarkerKind kind) => kind switch
    {
        MarkerKind.CargoShip => "cargo",
        MarkerKind.PatrolHelicopter => "patrol",
        MarkerKind.Chinook => "ch47",
        MarkerKind.TravellingVendor => "vendor",
        _ => null,
    };

    private static Image<Rgba32>? Native(string? key) => key is null
        ? null
        : Cache.GetOrAdd(key, static k => k switch
        {
            "patrol" => MarkerIconComposer.Heli(),
            "ch47" => MarkerIconComposer.Chinook(),
            _ => LoadPng(k),
        });

    private static Image<Rgba32>? LoadPng(string key)
    {
        var asm = typeof(MapIcons).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourcePrefix + key + ".png");
        return stream is null ? null : Image.Load<Rgba32>(stream);
    }

    private static Image<Rgba32>? Scaled(string? key, int size) => key is null
        ? null
        : Cache.GetOrAdd($"{key}@{size}", _ =>
        {
            var native = Native(key);
            return native?.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max, Size = new Size(size, size),
            }));
        });
}
