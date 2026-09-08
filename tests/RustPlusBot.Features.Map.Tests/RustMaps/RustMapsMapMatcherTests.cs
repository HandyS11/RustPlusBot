using System.Text.Json;
using RustMapsApi.V4.Models;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.RustMaps;

namespace RustPlusBot.Features.Map.Tests.RustMaps;

/// <summary>
/// Ground-truth fixtures from the live incident: WEREWOLF GAMING reports size 3700 / seed 1900693728 over
/// Rust+ but runs a pre-generated level, so the RustMaps render for that key is a different island. The
/// bot posted it as the #info map.
/// </summary>
public sealed class RustMapsMapMatcherTests
{
    private const uint WorldSize = 3700;

    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public void Custom_map_server_is_reported_as_a_mismatch()
    {
        var verdict = RustMapsMapMatcher.Compare(RustMapsMonuments(), ServerMonuments(), WorldSize);

        Assert.Equal(RustMapsMapMatch.Mismatch, verdict);
    }

    [Fact]
    public void Same_world_is_reported_as_a_match()
    {
        // The same monuments the render was built from, expressed the way Rust+ sends them (map-corner
        // origin instead of the game's centred world origin). Same world → every monument is accounted for.
        var rustMaps = RustMapsMonuments();
        var asServerReportsThem = rustMaps.ConvertAll(m =>
            new MonumentSnapshot("token", m.Coordinates!.X + (WorldSize / 2f), m.Coordinates.Y + (WorldSize / 2f)));

        var verdict = RustMapsMapMatcher.Compare(rustMaps, asServerReportsThem, WorldSize);

        Assert.Equal(RustMapsMapMatch.Match, verdict);
    }

    [Fact]
    public void Small_offsets_still_match()
    {
        // Rust+ and RustMaps agree to the metre on the same world, but the verdict must not hinge on exact
        // equality: a monument shifted well inside the radius is still the same monument.
        var rustMaps = RustMapsMonuments();
        var shifted = rustMaps.ConvertAll(m => new MonumentSnapshot("token",
            m.Coordinates!.X + (WorldSize / 2f) + 10f, m.Coordinates.Y + (WorldSize / 2f) - 10f));

        Assert.Equal(RustMapsMapMatch.Match, RustMapsMapMatcher.Compare(rustMaps, shifted, WorldSize));
    }

    [Fact]
    public void Forgetting_the_origin_shift_would_be_caught()
    {
        // Guards the coordinate convention itself: feeding RustMaps' centred coordinates through as if they
        // were Rust+ map-corner coordinates must NOT read as a match.
        var rustMaps = RustMapsMonuments();
        var unshifted =
            rustMaps.ConvertAll(m => new MonumentSnapshot("token", m.Coordinates!.X, m.Coordinates.Y));

        Assert.Equal(RustMapsMapMatch.Mismatch, RustMapsMapMatcher.Compare(rustMaps, unshifted, WorldSize));
    }

    [Fact]
    public void Too_few_server_monuments_is_undecided()
    {
        var few = ServerMonuments().Take(RustMapsMapMatcher.MinimumSample - 1).ToList();

        Assert.Equal(RustMapsMapMatch.Unknown, RustMapsMapMatcher.Compare(RustMapsMonuments(), few, WorldSize));
    }

    [Fact]
    public void No_rustmaps_monuments_is_undecided()
    {
        Assert.Equal(RustMapsMapMatch.Unknown, RustMapsMapMatcher.Compare([], ServerMonuments(), WorldSize));
        Assert.Equal(RustMapsMapMatch.Unknown, RustMapsMapMatcher.Compare(null, ServerMonuments(), WorldSize));
    }

    [Fact]
    public void No_server_monuments_is_undecided()
    {
        Assert.Equal(RustMapsMapMatch.Unknown, RustMapsMapMatcher.Compare(RustMapsMonuments(), null, WorldSize));
    }

    [Fact]
    public void Unknown_world_size_is_undecided()
    {
        Assert.Equal(RustMapsMapMatch.Unknown,
            RustMapsMapMatcher.Compare(RustMapsMonuments(), ServerMonuments(), worldSize: 0));
    }

    private static List<Monument> RustMapsMonuments()
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FixtureDir, "rustmaps-3700-1900693728.json")));
        return
        [
            .. doc.RootElement.GetProperty("monuments").EnumerateArray().Select(m =>
            {
                var c = m.GetProperty("coordinates");
                return new Monument
                {
                    Coordinates = new Coordinates(c.GetProperty("x").GetInt32(), c.GetProperty("y").GetInt32())
                };
            })
        ];
    }

    private static List<MonumentSnapshot> ServerMonuments()
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FixtureDir, "rustplus-monuments-3700-1900693728.json")));
        return
        [
            .. doc.RootElement.EnumerateArray()
                .Select(m => new MonumentSnapshot(m.GetProperty("token").GetString()!,
                    m.GetProperty("x").GetSingle(), m.GetProperty("y").GetSingle()))
        ];
    }
}
