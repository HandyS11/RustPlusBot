namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>Identity of a RustMaps procedural map, shared by every server on a wipe.</summary>
/// <param name="Size">The world size.</param>
/// <param name="Seed">The map seed.</param>
public sealed record RustMapsMapKey(int Size, int Seed);
