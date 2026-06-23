using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
    public static Image<Rgba32>? Marker(MarkerKind kind) => kind switch
    {
        MarkerKind.CargoShip => Load("cargo"),
        MarkerKind.PatrolHelicopter => Load("patrol"),
        MarkerKind.Chinook => Load("ch47"),
        MarkerKind.TravellingVendor => Load("vendor"),
        _ => null,
    };

    /// <summary>Gets the rig icon for a rig kind, or null when there is no icon for that kind.</summary>
    /// <param name="kind">Which rig.</param>
    /// <param name="active">Whether the rig is currently active (reserved; activation styling is applied by the renderer).</param>
    /// <returns>The cached rig icon image, or null.</returns>
    /// <remarks>
    /// The <paramref name="active"/> parameter is reserved for future use: the renderer is responsible
    /// for overlaying activation styling; this method always returns the base icon regardless of state.
    /// </remarks>
#pragma warning disable RCS1163 // Unused parameter — 'active' is part of the public API contract; activation styling is applied by the renderer, not here.
    public static Image<Rgba32>? Rig(RigKind kind, bool active) => kind switch
    {
        RigKind.Small => Load("oilrig"),
        RigKind.Large => Load("largeoilrig"),
        _ => null,
    };
#pragma warning restore RCS1163

    /// <summary>Gets the travelling vendor icon.</summary>
    /// <returns>The cached vendor icon image, or null.</returns>
    public static Image<Rgba32>? Vendor() => Load("vendor");

    /// <summary>Gets the player position icon, or null when the asset is missing.</summary>
    /// <returns>The player icon, or null.</returns>
    public static Image<Rgba32>? Player() => Load("player");

    /// <summary>Gets the icon for a monument token, or null when the token is unmapped or the file is missing.</summary>
    /// <param name="token">The Rust+ monument protobuf token.</param>
    /// <returns>The cached icon image, or null.</returns>
    public static Image<Rgba32>? Monument(string token)
    {
        var key = MonumentIconMap.IconKeyFor(token);
        return key is null ? null : Load(key);
    }

    private static Image<Rgba32>? Load(string key) => Cache.GetOrAdd(key, static k =>
    {
        var asm = typeof(MapIcons).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourcePrefix + k + ".png");
        return stream is null ? null : Image.Load<Rgba32>(stream);
    });
}
