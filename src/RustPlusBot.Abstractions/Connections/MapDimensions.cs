namespace RustPlusBot.Abstractions.Connections;

/// <summary>The static-per-wipe map size needed to convert world coordinates to a grid reference.</summary>
/// <param name="Width">Map image width.</param>
/// <param name="Height">Map image height.</param>
/// <param name="OceanMargin">The ocean margin around the playable area.</param>
public sealed record MapDimensions(uint Width, uint Height, int OceanMargin);
