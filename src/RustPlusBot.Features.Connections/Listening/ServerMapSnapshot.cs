using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>
/// Everything Rust+ returns for one map query. The <c>GetMap</c> endpoint answers the map geometry, the
/// monument list and the base-map JPEG in a single response, so splitting them into separate calls makes
/// each reader pay for the whole ~683 KB image. This record keeps them together, letting a connected window
/// resolve the map with one round trip and serve every reader from it.
/// </summary>
/// <param name="Geometry">The map's pixel geometry, or null when the response was incomplete.</param>
/// <param name="Monuments">The map monuments (token + position); empty when the server sends none.</param>
/// <param name="JpgImage">The base-map JPEG bytes, or null when the server sends no image.</param>
internal sealed record ServerMapSnapshot(
    MapGeometry? Geometry,
    IReadOnlyList<MonumentSnapshot> Monuments,
    byte[]? JpgImage);

/// <summary>
/// The pixel geometry of the base-map image. Deliberately not <see cref="MapDimensions"/>: that also carries
/// the world size, which <c>GetMap</c> does not return. Pairing them here would tie the expensive map
/// download to a <c>GetInfo</c> round trip that fails independently of it.
/// </summary>
/// <param name="Width">Image width in pixels.</param>
/// <param name="Height">Image height in pixels.</param>
/// <param name="OceanMargin">Ocean border baked into the image, in pixels per side.</param>
internal sealed record MapGeometry(uint Width, uint Height, int OceanMargin);
