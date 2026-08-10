# Patrol Helicopter Crash Reporting and Off-Map Directions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Report a patrol helicopter that vanishes inside the map as a probable crash with its grid cell, and report any marker positioned outside the playable world by compass direction instead of a clamped edge grid cell.

**Architecture:** Two pure helpers land in `MapGrid` (Abstractions) — an 8-point bearing from the world centre, and two predicates for "outside the world" / "at or beyond the one-cell border band". A new `MapLocation` formatter in `Features.Events/Formatting` turns a coordinate into either a grid cell or a localized direction word and reports which it chose. Every renderer appends `.dir` to its message key when the location is a direction; the plain key keeps today's wording and now also covers the no-dimensions raw-coordinate fallback. `MarkerEventClassifier` splits heli removal into `HeliCrashed` / `HeliLeft` on the border-band predicate.

**Tech Stack:** .NET 10, C# with nullable reference types, xUnit + NSubstitute, RESX localization (`en` neutral + `fr` satellite), Discord.Net embeds.

**Spec:** `docs/superpowers/specs/2026-08-10-patrol-heli-crash-and-offmap-directions-design.md`

## Global Constraints

- `Directory.Build.props` sets `TreatWarningsAsErrors=true`, `GenerateDocumentationFile=true`, `AnalysisLevel=latest-all`, `EnforceCodeStyleInBuild=true`, `Nullable=enable`. **Every public type and public member needs an XML doc comment**, including `<param>` and `<returns>`, or the build fails. Internal members in this codebase are documented too — follow suit.
- Build and test with `dtk` (token-filtered `dotnet` wrapper): `dtk dotnet build`, `dtk dotnet test <csproj>`. Exit codes are preserved.
- Both `src/RustPlusBot.Localization/Strings.resx` (English, neutral) and `src/RustPlusBot.Localization/Strings.fr.resx` must gain the same keys. `StringsResourceParityTests` fails the build otherwise. Keep `<data>` entries in ordinal-alphabetical key order, matching the existing files.
- `ILocalizer.Get` returns the key itself when a key is missing (`ResxLocalizer.cs:25`) — it never throws. Assert on resolved text in tests, never on the key.
- Grid cell size is `MapGrid.CellSize` = 146.25 game units. Never hardcode 146.25 in production code.
- World axes: X runs west→east, Y runs south→north. Grid rows are numbered north→south.
- Commit messages follow conventional commits (`feat:`, `fix:`, `docs:`, `test:`).
- Do not change `GridReference`, `ServerTeamMessageRenderer`, or `PlayerEventRenderer`. Team members are always inside the world.

---

### Task 1: Compass direction and border-band math

**Files:**
- Create: `src/RustPlusBot.Abstractions/Connections/MapDirection.cs`
- Modify: `src/RustPlusBot.Abstractions/Connections/MapGrid.cs` (append members after `LabelFor`)
- Test: `tests/RustPlusBot.Abstractions.Tests/Connections/MapGridTests.cs` (append)

**Interfaces:**
- Consumes: `MapGrid.CellSize` (existing constant, 146.25f).
- Produces:
  - `enum MapDirection { North = 0, NorthEast = 1, East = 2, SouthEast = 3, South = 4, SouthWest = 5, West = 6, NorthWest = 7 }` in namespace `RustPlusBot.Abstractions.Connections`.
  - `static MapDirection MapGrid.DirectionFrom(float x, float y, uint worldSize)`
  - `static bool MapGrid.IsOutsideWorld(float x, float y, uint worldSize)`
  - `static bool MapGrid.IsAtOrBeyondBorder(float x, float y, uint worldSize)`

- [ ] **Step 1: Write the failing tests**

Append to `tests/RustPlusBot.Abstractions.Tests/Connections/MapGridTests.cs`, inside the existing `MapGridTests` class (the file already has `using RustPlusBot.Abstractions.Connections;`):

```csharp
    [Theory]
    [InlineData(2000f, 3900f, MapDirection.North)]
    [InlineData(3900f, 3900f, MapDirection.NorthEast)]
    [InlineData(3900f, 2000f, MapDirection.East)]
    [InlineData(3900f, 100f, MapDirection.SouthEast)]
    [InlineData(2000f, 100f, MapDirection.South)]
    [InlineData(100f, 100f, MapDirection.SouthWest)]
    [InlineData(100f, 2000f, MapDirection.West)]
    [InlineData(100f, 3900f, MapDirection.NorthWest)]
    public void DirectionFrom_bins_the_bearing_from_the_world_centre(float x, float y, MapDirection expected) =>
        Assert.Equal(expected, MapGrid.DirectionFrom(x, y, 4000u));

    [Theory]
    // Sectors are centred on each compass point, so the North/NorthEast split sits at 22.5°
    // clockwise from north: dx/dy = tan(22.5°) = 0.4142. With dy = 1000, that is dx = 414.2.
    [InlineData(2410f, 3000f, MapDirection.North)]
    [InlineData(2420f, 3000f, MapDirection.NorthEast)]
    public void DirectionFrom_splits_sectors_half_way_between_compass_points(
        float x, float y, MapDirection expected) =>
        Assert.Equal(expected, MapGrid.DirectionFrom(x, y, 4000u));

    [Fact]
    public void DirectionFrom_works_outside_the_world()
    {
        // The whole point of the helper: ocean spawns sit beyond the world bounds.
        Assert.Equal(MapDirection.NorthWest, MapGrid.DirectionFrom(-500f, 4500f, 4000u));
    }

    [Fact]
    public void DirectionFrom_returns_north_at_the_exact_centre() =>
        Assert.Equal(MapDirection.North, MapGrid.DirectionFrom(2000f, 2000f, 4000u));

    [Theory]
    [InlineData(0f, 0f, false)]
    [InlineData(4000f, 4000f, false)]
    [InlineData(-0.1f, 2000f, true)]
    [InlineData(2000f, 4000.1f, true)]
    public void IsOutsideWorld_treats_the_exact_edges_as_inside(float x, float y, bool expected) =>
        Assert.Equal(expected, MapGrid.IsOutsideWorld(x, y, 4000u));

    [Theory]
    [InlineData(2000f, 2000f, false)] // dead centre
    [InlineData(146.25f, 2000f, false)] // exactly one cell in from the west edge
    [InlineData(146f, 2000f, true)] // a hair inside the band
    [InlineData(2000f, 3854f, true)] // 4000 - 146.25 = 3853.75, so this is inside the north band
    [InlineData(-50f, 2000f, true)] // outside the world entirely
    public void IsAtOrBeyondBorder_covers_a_one_cell_band(float x, float y, bool expected) =>
        Assert.Equal(expected, MapGrid.IsAtOrBeyondBorder(x, y, 4000u));
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dtk dotnet test tests/RustPlusBot.Abstractions.Tests/RustPlusBot.Abstractions.Tests.csproj --filter "FullyQualifiedName~MapGridTests"
```

Expected: build failure — `MapDirection` does not exist, and `MapGrid` has no `DirectionFrom` / `IsOutsideWorld` / `IsAtOrBeyondBorder`.

- [ ] **Step 3: Create the direction enum**

Create `src/RustPlusBot.Abstractions/Connections/MapDirection.cs`:

```csharp
namespace RustPlusBot.Abstractions.Connections;

/// <summary>
/// An 8-point compass direction. Values are ordered clockwise from north so that a bearing can be
/// binned straight into this enum by integer division.
/// </summary>
public enum MapDirection
{
    /// <summary>Due north.</summary>
    North = 0,

    /// <summary>North-east.</summary>
    NorthEast = 1,

    /// <summary>Due east.</summary>
    East = 2,

    /// <summary>South-east.</summary>
    SouthEast = 3,

    /// <summary>Due south.</summary>
    South = 4,

    /// <summary>South-west.</summary>
    SouthWest = 5,

    /// <summary>Due west.</summary>
    West = 6,

    /// <summary>North-west.</summary>
    NorthWest = 7,
}
```

- [ ] **Step 4: Add the three helpers to MapGrid**

Append inside the `MapGrid` class in `src/RustPlusBot.Abstractions/Connections/MapGrid.cs`, after `LabelFor`:

```csharp
    /// <summary>Tests whether a coordinate falls outside the playable world.</summary>
    /// <param name="x">World X (west→east).</param>
    /// <param name="y">World Y (south→north).</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <returns>True when either axis is beyond <c>[0, worldSize]</c>; the exact edges count as inside.</returns>
    public static bool IsOutsideWorld(float x, float y, uint worldSize) =>
        x < 0f || y < 0f || x > worldSize || y > worldSize;

    /// <summary>
    /// Tests whether a coordinate sits at the map border — outside the world, or within one grid cell
    /// of any edge. Marker positions are sampled by polling, so a marker that has just crossed the
    /// border is usually still reported slightly inside it; the one-cell band absorbs that lag.
    /// </summary>
    /// <param name="x">World X (west→east).</param>
    /// <param name="y">World Y (south→north).</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <returns>True when the coordinate is outside the world or within <see cref="CellSize"/> of an edge.</returns>
    public static bool IsAtOrBeyondBorder(float x, float y, uint worldSize) =>
        IsOutsideWorld(x, y, worldSize)
        || x < CellSize
        || y < CellSize
        || x > worldSize - CellSize
        || y > worldSize - CellSize;

    /// <summary>Bins the bearing from the world centre to a coordinate into an 8-point compass direction.</summary>
    /// <param name="x">World X (west→east).</param>
    /// <param name="y">World Y (south→north).</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <returns>
    /// The compass sector containing the coordinate. Sectors are 45° wide and centred on each compass
    /// point, so due north spans 337.5°–22.5°. A coordinate exactly at the centre yields
    /// <see cref="MapDirection.North"/>; that cannot arise for a real off-map marker.
    /// </returns>
    public static MapDirection DirectionFrom(float x, float y, uint worldSize)
    {
        var centre = worldSize / 2f;

        // Atan2(east, north) gives a bearing measured clockwise from north, which is the order the
        // MapDirection values are declared in.
        var bearing = MathF.Atan2(x - centre, y - centre) * (180f / MathF.PI);
        if (bearing < 0f)
        {
            bearing += 360f;
        }

        // Shift by half a sector so the bins straddle each compass point rather than starting at it.
        return (MapDirection)(int)MathF.Floor((bearing + 22.5f) % 360f / 45f);
    }
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dtk dotnet test tests/RustPlusBot.Abstractions.Tests/RustPlusBot.Abstractions.Tests.csproj --filter "FullyQualifiedName~MapGridTests"
```

Expected: PASS, including the pre-existing `CellCount` / `ColumnLetters` / `LabelFor` tests.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Abstractions/Connections/MapDirection.cs \
        src/RustPlusBot.Abstractions/Connections/MapGrid.cs \
        tests/RustPlusBot.Abstractions.Tests/Connections/MapGridTests.cs
git commit -m "feat: add compass direction and border-band map grid helpers"
```

---

### Task 2: MapLocation formatter and direction words

**Files:**
- Create: `src/RustPlusBot.Features.Events/Formatting/MapLocation.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `src/RustPlusBot.Localization/Strings.fr.resx`
- Test: `tests/RustPlusBot.Features.Events.Tests/Formatting/MapLocationTests.cs` (create)

**Interfaces:**
- Consumes: `MapGrid.DirectionFrom`, `MapGrid.IsOutsideWorld` (Task 1); existing `GridReference.From(float, float, MapDimensions?, MapGridStyle)`; `ILocalizer.Get(string key, string culture)`.
- Produces:
  - `public readonly record struct MapLocationText(bool IsDirection, string Text)` in `RustPlusBot.Features.Events.Formatting`.
  - `static MapLocationText MapLocation.Describe(ILocalizer localizer, string culture, float x, float y, MapDimensions? dims, MapGridStyle style = MapGridStyle.InGame)`
  - `static MapLocationText MapLocation.DescribeDirection(ILocalizer localizer, string culture, float x, float y, MapDimensions? dims)`
  - Resource keys `direction.n`, `direction.ne`, `direction.e`, `direction.se`, `direction.s`, `direction.sw`, `direction.w`, `direction.nw`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.Features.Events.Tests/Formatting/MapLocationTests.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Events.Tests.Formatting;

public sealed class MapLocationTests
{
    private static readonly ResxLocalizer Loc = new();
    private static readonly MapDimensions Dims = new(4000u, 4000u, 500, WorldSize: 4000u);

    [Fact]
    public void Inside_the_world_describes_a_grid_cell()
    {
        var location = MapLocation.Describe(Loc, "en", 10f, 3990f, Dims);

        Assert.False(location.IsDirection);
        Assert.Equal("A0", location.Text);
    }

    [Fact]
    public void Outside_the_world_describes_a_direction()
    {
        var location = MapLocation.Describe(Loc, "en", -500f, 4500f, Dims);

        Assert.True(location.IsDirection);
        Assert.Equal("north-west", location.Text);
    }

    [Fact]
    public void Direction_words_are_localized()
    {
        // French direction words carry their article so one message value ("vers {0}") covers all eight.
        Assert.Equal("le nord-ouest", MapLocation.Describe(Loc, "fr", -500f, 4500f, Dims).Text);
        Assert.Equal("l'est", MapLocation.Describe(Loc, "fr", 4500f, 2000f, Dims).Text);
    }

    [Fact]
    public void Null_dimensions_fall_back_to_raw_coordinates()
    {
        var location = MapLocation.Describe(Loc, "en", 1234f, 5678f, dims: null);

        Assert.False(location.IsDirection);
        Assert.Equal("(1234, 5678)", location.Text);
    }

    [Fact]
    public void DescribeDirection_uses_a_direction_even_inside_the_world()
    {
        var location = MapLocation.DescribeDirection(Loc, "en", 2000f, 3900f, Dims);

        Assert.True(location.IsDirection);
        Assert.Equal("north", location.Text);
    }

    [Fact]
    public void DescribeDirection_falls_back_to_raw_coordinates_without_dimensions()
    {
        var location = MapLocation.DescribeDirection(Loc, "en", 1234f, 5678f, dims: null);

        Assert.False(location.IsDirection);
        Assert.Equal("(1234, 5678)", location.Text);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dtk dotnet test tests/RustPlusBot.Features.Events.Tests/RustPlusBot.Features.Events.Tests.csproj --filter "FullyQualifiedName~MapLocationTests"
```

Expected: build failure — `MapLocation` does not exist.

- [ ] **Step 3: Add the direction words to both RESX files**

In `src/RustPlusBot.Localization/Strings.resx`, insert in alphabetical position (after the `decay.*` / before the `event.*` entries — locate by searching for the first `<data name="e`):

```xml
  <data name="direction.e" xml:space="preserve">
    <value>east</value>
  </data>
  <data name="direction.n" xml:space="preserve">
    <value>north</value>
  </data>
  <data name="direction.ne" xml:space="preserve">
    <value>north-east</value>
  </data>
  <data name="direction.nw" xml:space="preserve">
    <value>north-west</value>
  </data>
  <data name="direction.s" xml:space="preserve">
    <value>south</value>
  </data>
  <data name="direction.se" xml:space="preserve">
    <value>south-east</value>
  </data>
  <data name="direction.sw" xml:space="preserve">
    <value>south-west</value>
  </data>
  <data name="direction.w" xml:space="preserve">
    <value>west</value>
  </data>
```

The same keys in `src/RustPlusBot.Localization/Strings.fr.resx`, in the same position:

```xml
  <data name="direction.e" xml:space="preserve">
    <value>l'est</value>
  </data>
  <data name="direction.n" xml:space="preserve">
    <value>le nord</value>
  </data>
  <data name="direction.ne" xml:space="preserve">
    <value>le nord-est</value>
  </data>
  <data name="direction.nw" xml:space="preserve">
    <value>le nord-ouest</value>
  </data>
  <data name="direction.s" xml:space="preserve">
    <value>le sud</value>
  </data>
  <data name="direction.se" xml:space="preserve">
    <value>le sud-est</value>
  </data>
  <data name="direction.sw" xml:space="preserve">
    <value>le sud-ouest</value>
  </data>
  <data name="direction.w" xml:space="preserve">
    <value>l'ouest</value>
  </data>
```

- [ ] **Step 4: Create the MapLocation formatter**

Create `src/RustPlusBot.Features.Events/Formatting/MapLocation.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Events.Formatting;

/// <summary>A rendered marker location, and whether it names a compass direction rather than a grid cell.</summary>
/// <param name="IsDirection">
///     True when <paramref name="Text"/> is a compass direction. False for a grid cell and for the
///     raw-coordinate fallback — it answers "does this text name a direction", which is the question
///     the <c>.dir</c> message-key suffix asks.
/// </param>
/// <param name="Text">The localized location text.</param>
public readonly record struct MapLocationText(bool IsDirection, string Text);

/// <summary>
///     Describes a marker position as a grid cell when it is on the map, and as a compass direction
///     when it is not. Markers spawn and despawn in the ocean outside the playable world, where a grid
///     reference would name a cell the marker is not in.
/// </summary>
public static class MapLocation
{
    /// <summary>Describes a position, preferring a grid cell and falling back to a direction off-map.</summary>
    /// <param name="localizer">Resolves the direction word.</param>
    /// <param name="culture">The guild culture ("en"/"fr").</param>
    /// <param name="x">World X coordinate.</param>
    /// <param name="y">World Y coordinate.</param>
    /// <param name="dims">Map dimensions, or null when unavailable.</param>
    /// <param name="style">Which grid convention to bin against.</param>
    /// <returns>A direction off-map, a grid cell on-map, or raw coordinates when dimensions are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="localizer"/> is null.</exception>
    public static MapLocationText Describe(
        ILocalizer localizer,
        string culture,
        float x,
        float y,
        MapDimensions? dims,
        MapGridStyle style = MapGridStyle.InGame)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        if (dims is null || dims.WorldSize == 0)
        {
            return new MapLocationText(false, GridReference.From(x, y, dims, style));
        }

        return MapGrid.IsOutsideWorld(x, y, dims.WorldSize)
            ? new MapLocationText(true, Word(localizer, culture, x, y, dims.WorldSize))
            : new MapLocationText(false, GridReference.From(x, y, dims, style));
    }

    /// <summary>
    ///     Describes a position as a compass direction regardless of whether it is on the map. Used by
    ///     departure messages, where the direction the marker headed matters more than the cell it was
    ///     last seen in.
    /// </summary>
    /// <param name="localizer">Resolves the direction word.</param>
    /// <param name="culture">The guild culture ("en"/"fr").</param>
    /// <param name="x">World X coordinate.</param>
    /// <param name="y">World Y coordinate.</param>
    /// <param name="dims">Map dimensions, or null when unavailable.</param>
    /// <returns>A direction, or raw coordinates when dimensions are unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="localizer"/> is null.</exception>
    public static MapLocationText DescribeDirection(
        ILocalizer localizer,
        string culture,
        float x,
        float y,
        MapDimensions? dims)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return dims is null || dims.WorldSize == 0
            ? new MapLocationText(false, GridReference.From(x, y, dims))
            : new MapLocationText(true, Word(localizer, culture, x, y, dims.WorldSize));
    }

    private static string Word(ILocalizer localizer, string culture, float x, float y, uint worldSize) =>
        localizer.Get(Key(MapGrid.DirectionFrom(x, y, worldSize)), culture);

    private static string Key(MapDirection direction) => direction switch
    {
        MapDirection.North => "direction.n",
        MapDirection.NorthEast => "direction.ne",
        MapDirection.East => "direction.e",
        MapDirection.SouthEast => "direction.se",
        MapDirection.South => "direction.s",
        MapDirection.SouthWest => "direction.sw",
        MapDirection.West => "direction.w",
        MapDirection.NorthWest => "direction.nw",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unsupported map direction."),
    };
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dtk dotnet test tests/RustPlusBot.Features.Events.Tests/RustPlusBot.Features.Events.Tests.csproj --filter "FullyQualifiedName~MapLocationTests"
dtk dotnet test tests/RustPlusBot.Localization.Tests/RustPlusBot.Localization.Tests.csproj
```

Expected: PASS both — including `StringsResourceParityTests`, which proves the eight keys landed in both files.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Events/Formatting/MapLocation.cs \
        src/RustPlusBot.Localization/Strings.resx \
        src/RustPlusBot.Localization/Strings.fr.resx \
        tests/RustPlusBot.Features.Events.Tests/Formatting/MapLocationTests.cs
git commit -m "feat: describe off-map marker positions by compass direction"
```

---

### Task 3: Classify a downed helicopter as a crash

**Files:**
- Modify: `src/RustPlusBot.Features.Events/Classifying/MapEventKind.cs`
- Modify: `src/RustPlusBot.Features.Events/Classifying/MarkerEventClassifier.cs:35-47`
- Test: `tests/RustPlusBot.Features.Events.Tests/Classifying/MarkerEventClassifierTests.cs` (append)

**Interfaces:**
- Consumes: `MapGrid.IsAtOrBeyondBorder` (Task 1).
- Produces: `MapEventKind.HeliCrashed = 5`. Tasks 4 and 5 both switch on it; their `switch` expressions throw `ArgumentOutOfRangeException` on unhandled kinds, so a missing arm is a test failure, not a silent fallback.

- [ ] **Step 1: Write the failing tests**

Append to the `MarkerEventClassifierTests` class:

```csharp
    [Fact]
    public void Heli_removed_inside_the_map_is_HeliCrashed()
    {
        // Dead centre of a 4000 world: nowhere near the border, so it came down here.
        var result = Build().Classify(Evt([],
            [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 2000f, 2000f, null)]));

        Assert.Equal(MapEventKind.HeliCrashed, Assert.Single(result).Kind);
    }

    [Fact]
    public void Heli_removed_within_one_cell_of_the_edge_is_HeliLeft()
    {
        // One cell is 146.25 units, so x = 100 is inside the border band: a routine departure.
        var result = Build().Classify(Evt([],
            [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 100f, 2000f, null)]));

        Assert.Equal(MapEventKind.HeliLeft, Assert.Single(result).Kind);
    }

    [Fact]
    public void Heli_removed_outside_the_world_is_HeliLeft()
    {
        var result = Build().Classify(Evt([],
            [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 4500f, 2000f, null)]));

        Assert.Equal(MapEventKind.HeliLeft, Assert.Single(result).Kind);
    }

    [Fact]
    public void Heli_removed_without_dimensions_is_HeliLeft()
    {
        // No world size means neither a cell nor a direction is computable: keep the old behaviour.
        var evt = new MapMarkersChangedEvent(1UL, Server, null, [],
            [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 2000f, 2000f, null)], []);

        Assert.Equal(MapEventKind.HeliLeft, Assert.Single(Build().Classify(evt)).Kind);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dtk dotnet test tests/RustPlusBot.Features.Events.Tests/RustPlusBot.Features.Events.Tests.csproj --filter "FullyQualifiedName~MarkerEventClassifierTests"
```

Expected: build failure — `MapEventKind.HeliCrashed` does not exist.

- [ ] **Step 3: Add the enum member**

Append to `src/RustPlusBot.Features.Events/Classifying/MapEventKind.cs`, inside the enum after `ChinookSpawned = 4`:

```csharp

    /// <summary>A patrol helicopter disappeared inside the map — almost certainly shot down.</summary>
    HeliCrashed = 5,
```

- [ ] **Step 4: Split heli removal in the classifier**

In `src/RustPlusBot.Features.Events/Classifying/MarkerEventClassifier.cs`, add the `MapGrid` using if absent (`using RustPlusBot.Abstractions.Connections;` is already there), then change the removal loop's switch arm and add a private helper:

```csharp
        foreach (var m in evt.Removed)
        {
            MapEventKind? kind = m.Kind switch
            {
                MarkerKind.CargoShip => MapEventKind.CargoLeft,
                MarkerKind.PatrolHelicopter => HeliRemoval(m, evt.Dimensions),
                _ => null, // Chinook/Crate removal is silent.
            };
            if (kind is { } k)
            {
                events.Add(new RustMapEvent(k, m.X, m.Y, evt.Dimensions, now));
            }
        }

        return events;
    }

    // The heli marker vanishes either because players shot it down or because it finished its patrol
    // and flew off the map. Polling samples position, so a heli that has just crossed the border is
    // usually still reported slightly inside it — hence the one-cell band rather than a strict
    // inside/outside test, which would misreport most routine departures as crashes.
    private static MapEventKind HeliRemoval(MapMarkerSnapshot marker, MapDimensions? dims) =>
        dims is null || dims.WorldSize == 0 || MapGrid.IsAtOrBeyondBorder(marker.X, marker.Y, dims.WorldSize)
            ? MapEventKind.HeliLeft
            : MapEventKind.HeliCrashed;
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dtk dotnet test tests/RustPlusBot.Features.Events.Tests/RustPlusBot.Features.Events.Tests.csproj --filter "FullyQualifiedName~MarkerEventClassifierTests"
```

Expected: PASS, including the pre-existing `Heli_added_and_removed_map_to_entered_and_left` and `Multiple_deltas_produce_multiple_events` — both remove the heli at (0, 0), which is at the border and so stays `HeliLeft`.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Events/Classifying/MapEventKind.cs \
        src/RustPlusBot.Features.Events/Classifying/MarkerEventClassifier.cs \
        tests/RustPlusBot.Features.Events.Tests/Classifying/MarkerEventClassifierTests.cs
git commit -m "feat: classify a helicopter lost inside the map as a crash"
```

---

### Task 4: Announcement embeds and team-chat lines

**Files:**
- Modify: `src/RustPlusBot.Features.Events/Rendering/EventEmbedRenderer.cs:20-39,76-90`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `src/RustPlusBot.Localization/Strings.fr.resx`
- Test: `tests/RustPlusBot.Features.Events.Tests/Rendering/EventEmbedRendererTests.cs` (append)

**Interfaces:**
- Consumes: `MapLocation.Describe` / `MapLocation.DescribeDirection` / `MapLocationText` (Task 2), `MapEventKind.HeliCrashed` (Task 3), existing `GridReference.From`.
- Produces: nothing new for later tasks. `EventEmbedRenderer.Render` and `RenderLine` keep their existing signatures.

**Key convention (applies here and in Task 5):** the renderer resolves a base key, then appends `.dir` when the described location is a direction. Line keys are `<base>.line` and `<base>.line.dir`. `HeliCrashed` always uses a grid cell, so it needs no `.dir` variant.

- [ ] **Step 1: Write the failing tests**

Append to the `EventEmbedRendererTests` class:

```csharp
    private static readonly MapDimensions Dims4000 = new(4000u, 4000u, 500, WorldSize: 4000u);

    [Fact]
    public void Heli_crashed_renders_a_grid_cell()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.HeliCrashed, 2000f, 2000f, Dims4000, Now), "en");

        Assert.Equal("🚁 Patrol Helicopter probably crashed at N13", embed.Description);
    }

    [Fact]
    public void Heli_crashed_renders_french()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.HeliCrashed, 2000f, 2000f, Dims4000, Now), "fr");

        Assert.Contains("probablement abattu en", embed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Heli_left_renders_a_direction()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.HeliLeft, 100f, 2000f, Dims4000, Now), "en");

        Assert.Equal("🚁 Patrol Helicopter left the map to the west", embed.Description);
    }

    [Fact]
    public void Cargo_entered_off_map_renders_a_direction_not_a_clamped_cell()
    {
        // The bug being fixed: an ocean spawn outside the world used to report the clamped edge cell.
        var embed = Build().Render(new RustMapEvent(MapEventKind.CargoEntered, 4500f, 4500f, Dims4000, Now), "en");

        Assert.Equal("🚢 Cargo Ship entered from the north-east", embed.Description);
    }

    [Fact]
    public void Cargo_entered_on_map_still_renders_a_cell()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.CargoEntered, 10f, 3990f, Dims4000, Now), "en");

        Assert.Equal("🚢 Cargo Ship entered at A0", embed.Description);
    }

    [Fact]
    public void Left_without_dimensions_keeps_the_raw_coordinate_wording()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.HeliLeft, 1234f, 5678f, null, Now), "en");

        Assert.Equal("🚁 Patrol Helicopter left ((1234, 5678))", embed.Description);
    }

    [Fact]
    public void Lines_follow_the_same_direction_split()
    {
        var renderer = Build();

        Assert.Equal("Patrol Helicopter probably crashed at N13",
            renderer.RenderLine(new RustMapEvent(MapEventKind.HeliCrashed, 2000f, 2000f, Dims4000, Now), "en"));
        Assert.Equal("Chinook spawned to the south-west",
            renderer.RenderLine(new RustMapEvent(MapEventKind.ChinookSpawned, -100f, -100f, Dims4000, Now), "en"));
        Assert.Equal("Cargo Ship left the map to the north-east",
            renderer.RenderLine(new RustMapEvent(MapEventKind.CargoLeft, 4500f, 4500f, Dims4000, Now), "en"));
    }
```

Where `N13` comes from: a 4000 world has 28 cells (`4000 / 146.25 = 27.35` → 27 whole + 1 partial edge cell). Centre (2000, 2000) → column `floor(2000 / 146.25) = 13` → "N"; row `floor((4000 - 0 - 2000) / 146.25) = 13` (in-game style has no row inset).

- [ ] **Step 2: Run tests to verify they fail**

```bash
dtk dotnet test tests/RustPlusBot.Features.Events.Tests/RustPlusBot.Features.Events.Tests.csproj --filter "FullyQualifiedName~EventEmbedRendererTests"
```

Expected: FAIL — the crashed tests fail to build or report the raw key, and the direction tests report clamped cells.

- [ ] **Step 3: Add the event strings to both RESX files**

In `src/RustPlusBot.Localization/Strings.resx`, alongside the existing `event.*` entries, keeping alphabetical order:

```xml
  <data name="event.cargo.entered.dir" xml:space="preserve">
    <value>🚢 Cargo Ship entered from the {0}</value>
  </data>
  <data name="event.cargo.entered.line.dir" xml:space="preserve">
    <value>Cargo Ship entered from the {0}</value>
  </data>
  <data name="event.cargo.left.dir" xml:space="preserve">
    <value>🚢 Cargo Ship left the map to the {0}</value>
  </data>
  <data name="event.cargo.left.line.dir" xml:space="preserve">
    <value>Cargo Ship left the map to the {0}</value>
  </data>
  <data name="event.chinook.spawned.dir" xml:space="preserve">
    <value>🚁 Chinook spawned to the {0}</value>
  </data>
  <data name="event.chinook.spawned.line.dir" xml:space="preserve">
    <value>Chinook spawned to the {0}</value>
  </data>
  <data name="event.heli.crashed" xml:space="preserve">
    <value>🚁 Patrol Helicopter probably crashed at {0}</value>
  </data>
  <data name="event.heli.crashed.line" xml:space="preserve">
    <value>Patrol Helicopter probably crashed at {0}</value>
  </data>
  <data name="event.heli.entered.dir" xml:space="preserve">
    <value>🚁 Patrol Helicopter entered from the {0}</value>
  </data>
  <data name="event.heli.entered.line.dir" xml:space="preserve">
    <value>Patrol Helicopter entered from the {0}</value>
  </data>
  <data name="event.heli.left.dir" xml:space="preserve">
    <value>🚁 Patrol Helicopter left the map to the {0}</value>
  </data>
  <data name="event.heli.left.line.dir" xml:space="preserve">
    <value>Patrol Helicopter left the map to the {0}</value>
  </data>
```

The same keys in `src/RustPlusBot.Localization/Strings.fr.resx`:

```xml
  <data name="event.cargo.entered.dir" xml:space="preserve">
    <value>🚢 Cargo Ship arrivé depuis {0}</value>
  </data>
  <data name="event.cargo.entered.line.dir" xml:space="preserve">
    <value>Cargo Ship arrivé depuis {0}</value>
  </data>
  <data name="event.cargo.left.dir" xml:space="preserve">
    <value>🚢 Cargo Ship parti vers {0}</value>
  </data>
  <data name="event.cargo.left.line.dir" xml:space="preserve">
    <value>Cargo Ship parti vers {0}</value>
  </data>
  <data name="event.chinook.spawned.dir" xml:space="preserve">
    <value>🚁 Chinook apparu vers {0}</value>
  </data>
  <data name="event.chinook.spawned.line.dir" xml:space="preserve">
    <value>Chinook apparu vers {0}</value>
  </data>
  <data name="event.heli.crashed" xml:space="preserve">
    <value>🚁 Hélicoptère de patrouille probablement abattu en {0}</value>
  </data>
  <data name="event.heli.crashed.line" xml:space="preserve">
    <value>Hélicoptère de patrouille probablement abattu en {0}</value>
  </data>
  <data name="event.heli.entered.dir" xml:space="preserve">
    <value>🚁 Hélicoptère de patrouille arrivé depuis {0}</value>
  </data>
  <data name="event.heli.entered.line.dir" xml:space="preserve">
    <value>Hélicoptère de patrouille arrivé depuis {0}</value>
  </data>
  <data name="event.heli.left.dir" xml:space="preserve">
    <value>🚁 Hélicoptère de patrouille parti vers {0}</value>
  </data>
  <data name="event.heli.left.line.dir" xml:space="preserve">
    <value>Hélicoptère de patrouille parti vers {0}</value>
  </data>
```

Leave the existing `event.cargo.left`, `event.cargo.left.line`, `event.heli.left`, `event.heli.left.line` values untouched — they now render only when map dimensions are unavailable.

- [ ] **Step 4: Route the renderer through MapLocation**

In `src/RustPlusBot.Features.Events/Rendering/EventEmbedRenderer.cs`, replace the bodies of `Render` and `RenderLine` and add a private `Locate` helper. `Render` becomes:

```csharp
    public Embed Render(RustMapEvent evt, string culture, MapGridStyle gridStyle = MapGridStyle.InGame)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var (key, suffix, text) = Locate(evt, culture, gridStyle);

        return new EmbedBuilder()
            .WithAuthor(localizer.Get("event.title", culture))
            .WithDescription(localizer.Get(key + suffix, culture, text))
            .WithTimestamp(evt.AtUtc)
            .Build();
    }
```

`RenderLine` becomes:

```csharp
    public string RenderLine(RustMapEvent evt, string culture, MapGridStyle gridStyle = MapGridStyle.InGame)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var (key, suffix, text) = Locate(evt, culture, gridStyle);
        return localizer.Get(key + ".line" + suffix, culture, text);
    }
```

And add, next to the existing private `RigKey`:

```csharp
    // Departures report the direction the marker headed, which outlives the cell it was last seen in;
    // a crash always happened inside the map, so it reports a cell. Everything else prefers a cell and
    // falls back to a direction only when the marker is outside the world. The ".dir" suffix picks the
    // matching message wording — with no map dimensions the location is raw coordinates, IsDirection is
    // false, and the plain key keeps today's text.
    private (string Key, string Suffix, string Text) Locate(RustMapEvent evt, string culture, MapGridStyle style)
    {
        var location = evt.Kind switch
        {
            MapEventKind.CargoLeft or MapEventKind.HeliLeft =>
                MapLocation.DescribeDirection(localizer, culture, evt.X, evt.Y, evt.Dimensions),
            MapEventKind.HeliCrashed =>
                new MapLocationText(false, GridReference.From(evt.X, evt.Y, evt.Dimensions, style)),
            _ => MapLocation.Describe(localizer, culture, evt.X, evt.Y, evt.Dimensions, style),
        };

        var key = evt.Kind switch
        {
            MapEventKind.CargoEntered => "event.cargo.entered",
            MapEventKind.CargoLeft => "event.cargo.left",
            MapEventKind.HeliEntered => "event.heli.entered",
            MapEventKind.HeliLeft => "event.heli.left",
            MapEventKind.HeliCrashed => "event.heli.crashed",
            MapEventKind.ChinookSpawned => "event.chinook.spawned",
            _ => throw new ArgumentOutOfRangeException(nameof(evt), evt.Kind, "Unsupported map event kind."),
        };

        return (key, location.IsDirection ? ".dir" : string.Empty, location.Text);
    }
```

Delete the now-unused `grid` locals and per-method key switches from `Render` and `RenderLine`. Leave `RenderRig` and `RenderRigLine` alone. Keep the `<exception>` doc tags on both methods — `Locate` still throws for an unsupported kind.

- [ ] **Step 5: Run tests to verify they pass**

```bash
dtk dotnet test tests/RustPlusBot.Features.Events.Tests/RustPlusBot.Features.Events.Tests.csproj
dtk dotnet test tests/RustPlusBot.Localization.Tests/RustPlusBot.Localization.Tests.csproj
```

Expected: PASS. The pre-existing `Cargo_entered_renders_english_with_grid`, `Chinook_spawned_renders_french` and `Null_dimensions_render_raw_coordinates` must still pass unchanged.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Events/Rendering/EventEmbedRenderer.cs \
        src/RustPlusBot.Localization/Strings.resx \
        src/RustPlusBot.Localization/Strings.fr.resx \
        tests/RustPlusBot.Features.Events.Tests/Rendering/EventEmbedRendererTests.cs
git commit -m "feat: announce heli crashes and off-map directions in event embeds"
```

---

### Task 5: Commands (`!events`, `!cargo`, `!heli`, `!chinook`)

**Files:**
- Modify: `src/RustPlusBot.Features.Commands/Handlers/EventsCommandHandler.cs:35-50`
- Modify: `src/RustPlusBot.Features.Commands/Handlers/MarkerReply.cs:44-47`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `src/RustPlusBot.Localization/Strings.fr.resx`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/EventHandlersTests.cs` (append)

**Interfaces:**
- Consumes: `MapLocation.Describe` / `MapLocation.DescribeDirection` (Task 2), `MapEventKind.HeliCrashed` (Task 3). `MarkerReply.ForAsync` keeps its existing signature and its `{prefix}.ok` / `{prefix}.none` key convention, now with a `{prefix}.ok.dir` variant.
- Produces: nothing for later tasks.

- [ ] **Step 1: Write the failing tests**

Append to the `EventHandlersTests` class:

```csharp
    private static readonly MapDimensions Dims4000 = new(4000u, 4000u, 500, WorldSize: 4000u);

    [Fact]
    public async Task Heli_off_the_map_reports_a_direction()
    {
        var (clock, loc) = Deps();
        var state = Substitute.For<IEventState>();
        state.GetActiveMarkers(Guild, Server, MarkerKind.PatrolHelicopter).Returns(
        [
            new ActiveMarker(1, MarkerKind.PatrolHelicopter, 4500f, 4500f, Dims4000, Now.AddMinutes(-5),
                [new TrailPoint(4500f, 4500f)], null)
        ]);

        var reply = await new HeliCommandHandler(state, loc, clock, Settings()).ExecuteAsync(Ctx(),
            CancellationToken.None);

        Assert.Equal("Patrol Helicopter to the north-east (5m ago)", reply);
    }

    [Fact]
    public async Task Events_reports_a_crash_and_an_off_map_spawn()
    {
        var (_, loc) = Deps();
        var state = Substitute.For<IEventState>();
        state.GetRecentEvents(Guild, Server).Returns(
        [
            new RustMapEvent(MapEventKind.HeliCrashed, 2000f, 2000f, Dims4000, Now),
            new RustMapEvent(MapEventKind.CargoEntered, 4500f, 4500f, Dims4000, Now)
        ]);

        var reply = await new EventsCommandHandler(state, loc, Settings()).ExecuteAsync(Ctx(),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("heli crashed in", reply, StringComparison.Ordinal);
        Assert.Contains("cargo from the north-east", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_off_map_departure_reports_a_direction()
    {
        var (_, loc) = Deps();
        var state = Substitute.For<IEventState>();
        state.GetRecentEvents(Guild, Server).Returns(
        [
            new RustMapEvent(MapEventKind.CargoLeft, -500f, 100f, Dims4000, Now)
        ]);

        var reply = await new EventsCommandHandler(state, loc, Settings()).ExecuteAsync(Ctx(),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("cargo left to the south-west", reply, StringComparison.Ordinal);
    }
```

The `5m ago` comes from `DurationFormat.Compact(TimeSpan.FromMinutes(5))`, which renders sub-hour spans as `"{totalMinutes}m"` — the marker is stamped `Now.AddMinutes(-5)` and the fixture clock returns `Now`.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dtk dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~EventHandlersTests"
```

Expected: FAIL — clamped grid cells instead of directions, and a literal `command.event.helicrashed` key or an `ArgumentOutOfRangeException` for the crash event.

- [ ] **Step 3: Add the command strings to both RESX files**

`src/RustPlusBot.Localization/Strings.resx`, in alphabetical position among the existing `command.*` entries:

```xml
  <data name="command.cargo.ok.dir" xml:space="preserve">
    <value>Cargo Ship to the {0} ({1} ago)</value>
  </data>
  <data name="command.chinook.ok.dir" xml:space="preserve">
    <value>Chinook to the {0} ({1} ago)</value>
  </data>
  <data name="command.event.cargoentered.dir" xml:space="preserve">
    <value>cargo from the {0}</value>
  </data>
  <data name="command.event.cargoleft.dir" xml:space="preserve">
    <value>cargo left to the {0}</value>
  </data>
  <data name="command.event.chinookspawned.dir" xml:space="preserve">
    <value>chinook to the {0}</value>
  </data>
  <data name="command.event.helicrashed" xml:space="preserve">
    <value>heli crashed in {0}</value>
  </data>
  <data name="command.event.helientered.dir" xml:space="preserve">
    <value>heli from the {0}</value>
  </data>
  <data name="command.event.helileft.dir" xml:space="preserve">
    <value>heli left to the {0}</value>
  </data>
  <data name="command.heli.ok.dir" xml:space="preserve">
    <value>Patrol Helicopter to the {0} ({1} ago)</value>
  </data>
```

`src/RustPlusBot.Localization/Strings.fr.resx`:

```xml
  <data name="command.cargo.ok.dir" xml:space="preserve">
    <value>Cargo vers {0} (il y a {1})</value>
  </data>
  <data name="command.chinook.ok.dir" xml:space="preserve">
    <value>Chinook vers {0} (il y a {1})</value>
  </data>
  <data name="command.event.cargoentered.dir" xml:space="preserve">
    <value>cargo depuis {0}</value>
  </data>
  <data name="command.event.cargoleft.dir" xml:space="preserve">
    <value>cargo parti vers {0}</value>
  </data>
  <data name="command.event.chinookspawned.dir" xml:space="preserve">
    <value>chinook vers {0}</value>
  </data>
  <data name="command.event.helicrashed" xml:space="preserve">
    <value>héli abattu en {0}</value>
  </data>
  <data name="command.event.helientered.dir" xml:space="preserve">
    <value>héli depuis {0}</value>
  </data>
  <data name="command.event.helileft.dir" xml:space="preserve">
    <value>héli parti vers {0}</value>
  </data>
  <data name="command.heli.ok.dir" xml:space="preserve">
    <value>Hélicoptère vers {0} (il y a {1})</value>
  </data>
```

- [ ] **Step 4: Route both handlers through MapLocation**

In `src/RustPlusBot.Features.Commands/Handlers/EventsCommandHandler.cs`, replace the `parts` projection:

```csharp
        var parts = events.Select(e =>
        {
            // Departures report the direction the marker headed; everything else prefers a grid cell
            // and falls back to a direction only when the marker is outside the world.
            var location = e.Kind is MapEventKind.CargoLeft or MapEventKind.HeliLeft
                ? MapLocation.DescribeDirection(localizer, context.Culture, e.X, e.Y, e.Dimensions)
                : MapLocation.Describe(localizer, context.Culture, e.X, e.Y, e.Dimensions, settings.GridStyle);

            var key = e.Kind switch
            {
                MapEventKind.CargoEntered => "command.event.cargoentered",
                MapEventKind.CargoLeft => "command.event.cargoleft",
                MapEventKind.HeliEntered => "command.event.helientered",
                MapEventKind.HeliLeft => "command.event.helileft",
                MapEventKind.HeliCrashed => "command.event.helicrashed",
                MapEventKind.ChinookSpawned => "command.event.chinookspawned",
                _ => throw new ArgumentOutOfRangeException(nameof(e), e.Kind, "Unsupported map event kind."),
            };

            return localizer.Get(key + (location.IsDirection ? ".dir" : string.Empty), context.Culture,
                location.Text);
        });
```

`MapEventKind.HeliCrashed` needs no `.dir` variant: a crash is inside the map by construction, so `Describe` returns a cell.

In `src/RustPlusBot.Features.Commands/Handlers/MarkerReply.cs`, replace the grid lookup and the return:

```csharp
        var m = markers[0];
        var location = MapLocation.Describe(localizer, context.Culture, m.X, m.Y, m.Dimensions, settings.GridStyle);
        var ago = DurationFormat.Compact(clock.UtcNow - m.SeenAtUtc);
        return localizer.Get($"{prefix}.ok{(location.IsDirection ? ".dir" : string.Empty)}", context.Culture,
            location.Text, ago);
```

Both files already import `RustPlusBot.Features.Events.Formatting`, so no new usings are needed. Remove the now-unused `GridReference` references if the compiler flags the import as unused.

- [ ] **Step 5: Run tests to verify they pass**

```bash
dtk dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj
dtk dotnet test tests/RustPlusBot.Localization.Tests/RustPlusBot.Localization.Tests.csproj
```

Expected: PASS. The pre-existing `Cargo_with_active_marker_reports_grid` uses (10, 3990) in a 4000 world — inside, so it still reports a cell.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Handlers/EventsCommandHandler.cs \
        src/RustPlusBot.Features.Commands/Handlers/MarkerReply.cs \
        src/RustPlusBot.Localization/Strings.resx \
        src/RustPlusBot.Localization/Strings.fr.resx \
        tests/RustPlusBot.Features.Commands.Tests/Handlers/EventHandlersTests.cs
git commit -m "feat: report crashes and off-map directions in marker commands"
```

---

### Task 6: `#info` events embed and README

**Files:**
- Modify: `src/RustPlusBot.Features.Events/Messages/ServerEventsMessageRenderer.cs:78-91`
- Modify: `README.md:62`
- Test: `tests/RustPlusBot.Features.Events.Tests/Messages/ServerEventsMessageRendererTests.cs` (append)

**Interfaces:**
- Consumes: `MapLocation.Describe` (Task 2).
- Produces: nothing. `server.events.out` needs no `.dir` variant — it is a compact "Out · {0} · {1} ago" field where a direction word reads correctly on its own.

- [ ] **Step 1: Write the failing test**

Append to the `ServerEventsMessageRendererTests` class:

```csharp
    [Fact]
    public async Task Off_map_marker_row_shows_a_direction()
    {
        var dims = new MapDimensions(4000u, 4000u, 500, WorldSize: 4000u);
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(1, ServerId, Arg.Any<MarkerKind>()).Returns([]);
        events.GetActiveMarkers(1, ServerId, MarkerKind.CargoShip).Returns(
        [
            new ActiveMarker(1, MarkerKind.CargoShip, 4500f, 4500f, dims, Now.AddMinutes(-3),
                [new TrailPoint(4500f, 4500f)], null)
        ]);
        var rigs = Substitute.For<IRigState>();
        rigs.Get(1, ServerId, Arg.Any<RigKind>()).Returns(new RigState(RigStatus.Online, null));

        var payload = await Build(events, rigs).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.NotNull(payload.Embed);
        var cargo = payload.Embed.Fields[0].Value;
        Assert.Contains("north-east", cargo, StringComparison.Ordinal);
    }
```

The cargo row is the first field — `RenderAsync` adds cargo, heli, chinook, small rig, large rig in that order.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dtk dotnet test tests/RustPlusBot.Features.Events.Tests/RustPlusBot.Features.Events.Tests.csproj --filter "FullyQualifiedName~ServerEventsMessageRendererTests"
```

Expected: FAIL — the row shows the clamped edge cell instead of "north-east".

- [ ] **Step 3: Route the marker row through MapLocation**

In `src/RustPlusBot.Features.Events/Messages/ServerEventsMessageRenderer.cs`, change the `Marker` helper's return:

```csharp
        // Newest-first: the freshest sighting is the one worth reporting.
        var marker = active[0];
        var location = MapLocation.Describe(localizer, culture, marker.X, marker.Y, marker.Dimensions, style);
        return localizer.Get("server.events.out", culture,
            location.Text,
            DurationFormat.Compact(clock.UtcNow - marker.SeenAtUtc));
```

`RustPlusBot.Features.Events.Formatting` is already imported. Drop the `GridReference` call it replaces.

- [ ] **Step 4: Run the test to verify it passes**

```bash
dtk dotnet test tests/RustPlusBot.Features.Events.Tests/RustPlusBot.Features.Events.Tests.csproj --filter "FullyQualifiedName~ServerEventsMessageRendererTests"
```

Expected: PASS, including the pre-existing `Renders_all_five_rows`.

- [ ] **Step 5: Update the README feature description**

In `README.md`, replace line 62's event bullet with:

```markdown
- Per-server `#events` feed (and an in-game team-chat mirror) for **Cargo Ship**, **Patrol Helicopter**, and **Chinook (CH47)** entering/leaving — a helicopter that disappears inside the map is reported as a probable crash with its grid cell, and markers outside the playable world are reported by compass direction rather than a map-edge grid cell — plus **small / large oil rig** activation, "crate lootable", and respawn, derived from polling the Rust+ map markers and monuments.
```

- [ ] **Step 6: Run the full test suite and build**

```bash
dtk dotnet build RustPlusBot.slnx
dtk dotnet test RustPlusBot.slnx
```

Expected: build succeeds with zero warnings (warnings are errors), all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Events/Messages/ServerEventsMessageRenderer.cs \
        README.md \
        tests/RustPlusBot.Features.Events.Tests/Messages/ServerEventsMessageRendererTests.cs
git commit -m "feat: show off-map directions in the #info events embed"
```

---

## Verification

After Task 6, the whole feature is in. Confirm against the spec:

- A heli marker removed at map centre produces "🚁 Patrol Helicopter probably crashed at N13" — Task 3 + Task 4.
- A heli marker removed within 146.25 units of an edge produces "🚁 Patrol Helicopter left the map to the west" — Task 3 + Task 4.
- A cargo marker added outside the world produces "🚢 Cargo Ship entered from the north-east", never a clamped cell — Task 4.
- `!heli` on an off-map heli replies "Patrol Helicopter to the north-east (5m ago)" — Task 5.
- The `#info` events embed shows "Out · north-east · 3m ago" for an off-map cargo — Task 6.
- With no map dimensions, every message keeps today's raw-coordinate wording — Tasks 3–5.
- `StringsResourceParityTests` passes, proving all 29 new keys exist in both languages — Tasks 2, 4, 5.
