namespace RustPlusBot.Abstractions.Map;

/// <summary>Read seam over the RustMaps generation state, for the #info map renderer (cross-assembly).</summary>
public interface IInfoMapReadModel
{
    /// <summary>
    /// The #info map state for one server's current (size, seed). A render is only offered once it has been
    /// verified against the monuments that server actually reports — a seed and a size do not identify a
    /// Rust world, so an unverified render may depict a different island entirely.
    /// </summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="size">The world size.</param>
    /// <param name="seed">The map generation seed.</param>
    /// <returns>The resolution; <see cref="InfoMapResolution.Pending"/> while nothing is decided.</returns>
    InfoMapResolution Resolve(ulong guildId, Guid serverId, int size, int seed);
}
