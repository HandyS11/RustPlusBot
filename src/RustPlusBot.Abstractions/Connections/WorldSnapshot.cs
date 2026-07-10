namespace RustPlusBot.Abstractions.Connections;

/// <summary>The world identity of a connected server, used to resolve external map imagery.</summary>
/// <param name="WorldSize">Size of the playable world, in game units.</param>
/// <param name="Seed">Procedural map generation seed.</param>
public sealed record WorldSnapshot(uint WorldSize, uint Seed);
