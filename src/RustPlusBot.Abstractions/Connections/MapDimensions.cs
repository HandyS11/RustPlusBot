namespace RustPlusBot.Abstractions.Connections;

/// <summary>Dimensions of the server-rendered map tile plus the world size.</summary>
/// <param name="Width">Width of the base map image, in pixels.</param>
/// <param name="Height">Height of the base map image, in pixels.</param>
/// <param name="OceanMargin">Ocean border baked into the base map image, in pixels.</param>
/// <param name="WorldSize">Size of the playable world, in game units (from server info).</param>
public sealed record MapDimensions(uint Width, uint Height, int OceanMargin, uint WorldSize);
