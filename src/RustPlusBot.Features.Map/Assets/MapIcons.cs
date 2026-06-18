using System.Collections.Concurrent;
using RustPlusBot.Features.Connections.Listening;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RustPlusBot.Features.Map.Assets;

/// <summary>
/// Loads vendored icon assets (embedded PNGs) once and serves them by marker kind or monument token.
/// Cached images are immutable inputs the renderer draws from; never mutate them.
/// </summary>
/// <remarks>
/// <c>MapIcons.Marker</c> does not include a <c>TravellingVendor</c> arm because
/// <c>MarkerKind.TravellingVendor</c> is added in Task 4.  The vendor PNG is already
/// embedded; Task 4 adds the switch arm once the enum member exists.
/// Use <see cref="Vendor"/> to access vendor.png until then.
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
        // MarkerKind.TravellingVendor => Load("vendor"),  -- Task 4: add arm when enum member is added
        _ => null,
    };

    /// <summary>Gets the travelling vendor icon (vendor.png). Used by Task 4 until MarkerKind.TravellingVendor is added to the switch.</summary>
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
