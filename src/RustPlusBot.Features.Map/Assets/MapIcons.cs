using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RustPlusBot.Features.Map.Assets;

/// <summary>
/// Loads vendored icon assets (embedded PNGs) once and serves them by marker kind or monument token.
/// Cached images are immutable inputs the renderer draws from; never mutate them.
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
        return key is null ? null : Load(key);
    }

    /// <summary>Gets the marker icon scaled to fit a square box, or null when the kind has no icon.</summary>
    /// <param name="kind">The marker kind.</param>
    /// <param name="size">The box edge length in pixels; the longest icon edge is scaled to it.</param>
    /// <returns>The cached scaled icon, or null.</returns>
    public static Image<Rgba32>? Marker(MarkerKind kind, int size) => Scaled(KeyFor(kind), size);

    /// <summary>Gets the rig icon for a rig kind, or null when there is no icon for that kind.</summary>
    /// <param name="kind">Which rig.</param>
    /// <param name="active">Whether the rig is currently active (reserved; activation styling is applied by the renderer).</param>
    /// <returns>The cached rig icon image, or null.</returns>
    /// <remarks>
    /// The <paramref name="active"/> parameter is reserved for future use: the renderer is responsible
    /// for overlaying activation styling; this method always returns the base icon regardless of state.
    /// </remarks>
#pragma warning disable RCS1163, IDE0060 // Unused parameter — 'active' is part of the public API contract; activation styling is applied by the renderer, not here.
    public static Image<Rgba32>? Rig(RigKind kind, bool active) => kind switch
    {
        RigKind.Small => Load("oilrig"),
        RigKind.Large => Load("largeoilrig"),
        _ => null,
    };
#pragma warning restore RCS1163, IDE0060

    /// <summary>Gets the rig icon scaled to fit a square box, or null when the kind has no icon.</summary>
    /// <param name="kind">Which rig.</param>
    /// <param name="active">Whether the rig is active (reserved; styling is applied by the renderer).</param>
    /// <param name="size">The box edge length in pixels.</param>
    /// <returns>The cached scaled icon, or null.</returns>
#pragma warning disable RCS1163, IDE0060 // 'active' is part of the API contract; styling is the renderer's job.
    public static Image<Rgba32>? Rig(RigKind kind, bool active, int size) =>
        Scaled(kind switch { RigKind.Small => "oilrig", RigKind.Large => "largeoilrig", _ => null }, size);
#pragma warning restore RCS1163, IDE0060

    /// <summary>Gets the travelling vendor icon.</summary>
    /// <returns>The cached vendor icon image, or null.</returns>
    public static Image<Rgba32>? Vendor() => Load("vendor");

    /// <summary>Gets the player position icon, or null when the asset is missing.</summary>
    /// <returns>The player icon, or null.</returns>
    public static Image<Rgba32>? Player() => Load("player");

    /// <summary>Gets the player icon scaled to fit a square box, or null when the asset is missing.</summary>
    /// <param name="size">The box edge length in pixels.</param>
    /// <returns>The cached scaled icon, or null.</returns>
    public static Image<Rgba32>? Player(int size) => Scaled("player", size);

    /// <summary>Gets the icon for a monument token, or null when the token is unmapped or the file is missing.</summary>
    /// <param name="token">The Rust+ monument protobuf token.</param>
    /// <returns>The cached icon image, or null.</returns>
    public static Image<Rgba32>? Monument(string token)
    {
        var key = MonumentIconMap.IconKeyFor(token);
        return key is null ? null : Load(key);
    }

    /// <summary>Gets the monument icon scaled to fit a square box, or null for unmapped tokens.</summary>
    /// <param name="token">The Rust+ monument protobuf token.</param>
    /// <param name="size">The box edge length in pixels.</param>
    /// <returns>The cached scaled icon, or null.</returns>
    public static Image<Rgba32>? Monument(string token, int size) => Scaled(MonumentIconMap.IconKeyFor(token), size);

    private static string? KeyFor(MarkerKind kind) => kind switch
    {
        MarkerKind.CargoShip => "cargo",
        MarkerKind.PatrolHelicopter => "patrol",
        MarkerKind.Chinook => "ch47",
        MarkerKind.TravellingVendor => "vendor",
        _ => null,
    };

    private static Image<Rgba32>? Load(string key) => Cache.GetOrAdd(key, static k =>
    {
        var asm = typeof(MapIcons).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourcePrefix + k + ".png");
        return stream is null ? null : Image.Load<Rgba32>(stream);
    });

    private static Image<Rgba32>? Scaled(string? key, int size) => key is null
        ? null
        : Cache.GetOrAdd($"{key}@{size}", _ =>
        {
            var native = Load(key);
            return native?.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max, Size = new Size(size, size),
            }));
        });
}
