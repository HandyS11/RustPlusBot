using RustMapsApi.V4.Models;
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>
/// Decides whether a RustMaps render for a (size, seed) is really the world a server runs.
/// </summary>
/// <remarks>
/// <para>
/// A seed and a world size do NOT identify a Rust world. A server can load a pre-generated or RustEdit'd
/// level file (its <c>GetInfo.Map</c> then reads like <c>procedural__3700_FHv7dBVBBUOBMxTGE8eiuw</c> rather
/// than <c>Procedural Map</c>) while <c>server.seed</c>/<c>server.worldsize</c> keep reporting leftover
/// config values; a server that stayed up across a map-gen change drifts the same way. RustMaps generates
/// from the seed, so in both cases it hands back a completely different island — which the bot used to post
/// as the #info map.
/// </para>
/// <para>
/// The check is a position fingerprint, not a name comparison: Rust+ monument tokens map many-to-one onto
/// <see cref="MonumentType"/> (both harbors collapse to one type, swamps and labs match by prefix), so
/// comparing types would report false mismatches. Both lists describe the same monument transforms when
/// the maps agree, so "does every monument the server reports sit on top of one RustMaps reports?" is
/// decisive and needs no naming at all. Coordinates differ only in origin: Rust+ sends map-corner-relative
/// coordinates (0..worldSize), RustMaps sends the game's own world coordinates (centred on 0).
/// </para>
/// <para>
/// The two verdicts are far apart, so the thresholds are not delicate: on identical maps the monuments are
/// the same values (agreement within metres), while on the mismatch this was written for only 11% of the
/// server's monuments had ANY RustMaps monument within <see cref="RadiusMeters"/> — RustMaps lists ~200
/// monuments over 3.7 km, so even unrelated maps score some coincidental hits.
/// </para>
/// </remarks>
public static class RustMapsMapMatcher
{
    /// <summary>How close a RustMaps monument must be to count as the same monument, in world metres.</summary>
    public const float RadiusMeters = 50f;

    /// <summary>Fraction of the server's monuments that must be accounted for to call it a match.</summary>
    public const double MatchedFractionThreshold = 0.5;

    /// <summary>Below this many server monuments the sample is too small to judge; the verdict stays Unknown.</summary>
    public const int MinimumSample = 8;

    /// <summary>Compares a RustMaps render's monuments against the ones a live server reports.</summary>
    /// <param name="rustMapsMonuments">Monuments from the RustMaps map info (world coordinates, centred on 0).</param>
    /// <param name="serverMonuments">Monuments from the server's Rust+ <c>GetMap</c> (0..worldSize).</param>
    /// <param name="worldSize">The server's world size, in game units.</param>
    /// <returns>The verdict; <see cref="RustMapsMapMatch.Unknown"/> when there is too little to judge.</returns>
    public static RustMapsMapMatch Compare(
        IReadOnlyList<Monument>? rustMapsMonuments,
        IReadOnlyList<MonumentSnapshot>? serverMonuments,
        uint worldSize)
    {
        if (rustMapsMonuments is not { Count: > 0 }
            || serverMonuments is not { Count: >= MinimumSample }
            || worldSize == 0)
        {
            return RustMapsMapMatch.Unknown;
        }

        var half = worldSize / 2f;
        var matched = serverMonuments.Count(m => IsNearAny(rustMapsMonuments, m.X - half, m.Y - half));

        return matched >= serverMonuments.Count * MatchedFractionThreshold
            ? RustMapsMapMatch.Match
            : RustMapsMapMatch.Mismatch;
    }

    private static bool IsNearAny(IReadOnlyList<Monument> rustMapsMonuments, float x, float y) =>
        rustMapsMonuments.Any(candidate => candidate.Coordinates is { } coordinates
                                           && Squared(coordinates.X - x, coordinates.Y - y)
                                           <= RadiusMeters * RadiusMeters);

    private static float Squared(float dx, float dy) => (dx * dx) + (dy * dy);
}
