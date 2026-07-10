namespace RustPlusBot.Abstractions.Map;

/// <summary>Read seam over the RustMaps generation state, for the #info map renderer (cross-assembly).</summary>
public interface IInfoMapReadModel
{
    /// <summary>The ready RustMaps map for a (size, seed), or null when it is not generated/ready yet.</summary>
    /// <param name="size">The world size.</param>
    /// <param name="seed">The map generation seed.</param>
    /// <returns>The ready map view, or null.</returns>
    InfoMapView? GetReady(int size, int seed);
}
