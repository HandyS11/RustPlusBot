# Map Render Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the four root causes of the broken #map render (wrong coordinate transform, unscaled icons, frozen markers, missing rotation plumbing) and add a RustMaps-preferred base-map source chain with fading motion trails.

**Architecture:** A new `MapProjection` value type implements the canonical world→pixel transform (worldSize + image pixel dims + ocean-margin px). A shared `MapGrid` in Abstractions unifies grid math for the renderer and `GridReference`. The existing marker-delta path gains a `Moved` bucket so `EventStateStore` keeps positions and a 6-deep history ring current. `BaseMapCache` becomes a source chain: RustMaps `RawImageUrl` (when an API key is configured) with the Rust+ JPEG as fallback.

**Tech Stack:** .NET 10, xunit + NSubstitute, SixLabors.ImageSharp 3.1.12 / Drawing 2.1.7 (Apache — never bump Drawing to 3.x), RustPlusApi 2.0.0-beta.3, RustMapsApi 1.0.0-beta.1.

**Spec:** `docs/superpowers/specs/2026-07-06-rustplusbot-map-render-rework-design.md`

## Global Constraints

- Solution file is `RustPlusBot.slnx` (no `.sln`). Build with `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` — zero warnings/errors required.
- **`-maxcpucount:1` is MANDATORY on EVERY `dotnet build` AND `dotnet test` command** — a ConfigureGitHooks target races on `.git/config` under parallel MSBuild and a broken build silently DROPS an assembly's tests (they report 0, look "passing"). Recurring trap; never omit it.
- `dotnet tool restore` before the first build (jb/ef are local tools).
- `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` is a hard CI gate — run it before the final commit of the branch (Task 12); it must produce no diff on CI.
- Run test assemblies **sequentially** (parallel `dotnet test` silently undercounts — recurring trap). Use `dotnet test <csproj> -maxcpucount:1` per assembly and read each assembly's own count.
- Tests are plain xUnit `Assert.*` + NSubstitute — NO FluentAssertions. `using Xunit` is global in test projects.
- `CultureInfo.InvariantCulture` / `StringComparison.Ordinal` on string ops (CA1305/CA1307/CA1310).
- ImageSharp stays `3.1.12`, ImageSharp.Drawing stays `2.1.7`.
- Baseline before this branch: 841 tests green / 17 assemblies on `develop` (post PR #44).
- `MapMarkerSnapshot.Rotation` stays `null` at the shim until RustPlusApi `2.0.0-beta.4` ships (see `RustPlusApi/docs/development/beta4-map-marker-rotation.md`); everything else must not depend on the lib bump.
- `docs/superpowers/` and `docs/product/` are gitignored — never `git add` them.
- XML doc comments on all public types/members (repo style); file-scoped namespaces; records for DTOs.
- Branch: `feat/map-render-rework` off `develop`.

**Before Task 1:** `git checkout develop && git pull && git checkout -b feat/map-render-rework`

---

### Task 1: `MapDimensions.WorldSize` + `WorldSnapshot`/`GetWorldAsync` seam

The projection and grid math need the real world size (`ServerInfo.MapSize`), and the RustMaps source needs the seed. Neither is plumbed today.

**Files:**

- Modify: `src/RustPlusBot.Abstractions/Connections/MapDimensions.cs`
- Create: `src/RustPlusBot.Abstractions/Connections/WorldSnapshot.cs`
- Modify: `src/RustPlusBot.Abstractions/Connections/IRustServerQuery.cs` (add `GetWorldAsync` after `GetMapDimensionsAsync`, line ~47)
- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs` (add `GetWorldAsync` after `GetMapDimensionsAsync`, line ~107)
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` (`GetMapDimensionsAsync` at ~524; new `GetWorldAsync` beside it; both inner-class `SocketConnection` members)
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (delegate `GetWorldAsync` beside `GetMapDimensionsAsync` at ~234)
- Modify: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` (implement new member)
- Modify (sweep): every `new MapDimensions(` call site — 16 hits in 13 files (`src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs`, `src/RustPlusBot.Features.Map/Composing/MapComposer.cs`, and 11 test files found via `grep -rn "new MapDimensions(" src tests --include="*.cs"`)
- Test: `tests/RustPlusBot.Features.Connections.Tests/ConnectionSupervisorTests.cs` (existing patterns)

**Interfaces:**

- Produces: `MapDimensions(uint Width, uint Height, int OceanMargin, uint WorldSize)` — Width/Height/OceanMargin are **pixels of the base JPEG**; WorldSize is **game units**. `WorldSnapshot(uint WorldSize, uint Seed)`. `Task<WorldSnapshot?> IRustServerQuery.GetWorldAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)`. `Task<WorldSnapshot?> IRustServerConnection.GetWorldAsync(TimeSpan timeout, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Extend `MapDimensions` and create `WorldSnapshot`**

```csharp
// MapDimensions.cs — replace the record declaration and fix the doc comment (values are PIXELS):
namespace RustPlusBot.Abstractions.Connections;

/// <summary>Dimensions of the server-rendered map tile plus the world size.</summary>
/// <param name="Width">Width of the base map image, in pixels.</param>
/// <param name="Height">Height of the base map image, in pixels.</param>
/// <param name="OceanMargin">Ocean border baked into the base map image, in pixels.</param>
/// <param name="WorldSize">Size of the playable world, in game units (from server info).</param>
public sealed record MapDimensions(uint Width, uint Height, int OceanMargin, uint WorldSize);
```

```csharp
// WorldSnapshot.cs (new):
namespace RustPlusBot.Abstractions.Connections;

/// <summary>The world identity of a connected server, used to resolve external map imagery.</summary>
/// <param name="WorldSize">Size of the playable world, in game units.</param>
/// <param name="Seed">Procedural map generation seed.</param>
public sealed record WorldSnapshot(uint WorldSize, uint Seed);
```

- [ ] **Step 2: Build to find every broken call site**

Run: `dotnet build RustPlusBot.slnx -warnaserror 2>&1 | grep -E "error" | head -40`
Expected: FAIL — CS7036 missing `WorldSize` at ~16 call sites.

- [ ] **Step 3: Sweep call sites**

In **production** code:

- `RustPlusSocketSource.GetMapDimensionsAsync` (~line 548): fetch info first, bail to null when `MapSize` missing:

```csharp
// Inside the existing try block, before "return new MapDimensions(...)":
var infoResponse = await _rustPlus.GetInfoAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
    .ConfigureAwait(false);
if (!infoResponse.IsSuccess || infoResponse.Data?.MapSize is not { } worldSize)
{
    return null;
}

return new MapDimensions(width, height, margin, worldSize);
```

- `MapComposer.ComposeAsync` dims-null fallback (~line 60): `new MapDimensions(0, 0, 0, 0)` (temporary — Task 5 replaces this branch entirely).

In **test** code: set `WorldSize` equal to the existing `Width` value (e.g. `new MapDimensions(4000, 4000, 500)` → `new MapDimensions(4000, 4000, 500, WorldSize: 4000)`). This keeps every existing grid/classifier/renderer expectation numerically identical because the old code treated Width as world units.

Watch for **target-typed** constructions the grep missed — the compiler finds them; the known one is `FakeRustSocketSource.cs:220` `DimensionsResult { get; set; } = new(4000u, 4000u, 500);` → `new(4000u, 4000u, 500, 4000u)` (a supervisor test at `ConnectionSupervisorTests.cs:406` re-states these exact values).

- [ ] **Step 4: Add the `GetWorldAsync` seam**

`IRustServerConnection` (after `GetMapDimensionsAsync`):

```csharp
/// <summary>Gets the world size and seed from server info, or null when unavailable.</summary>
/// <param name="timeout">The per-call timeout.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>The world snapshot, or null.</returns>
Task<WorldSnapshot?> GetWorldAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
```

`RustPlusSocketSource` inner connection (mirror the `GetMapDimensionsAsync` error-handling shape exactly — timeout CTS, `OperationCanceledException` → null, broad catch → `LogQueryFailed` + null):

```csharp
public async Task<WorldSnapshot?> GetWorldAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
{
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(timeout);
    try
    {
        var response = await _rustPlus.GetInfoAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccess || response.Data is not { MapSize: { } size, Seed: { } seed })
        {
            return null;
        }

        return new WorldSnapshot(size, seed);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return null;
    }
#pragma warning disable CA1031 // Broad catch: any map-query failure maps to null; never surface a token/secret.
    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
    {
        LogQueryFailed(_logger, ex);
        return null;
    }
}
```

`ConnectionSupervisor` (delegate exactly like `GetMapDimensionsAsync` at line 234 — resolve the live connection, return null when absent):

```csharp
/// <inheritdoc />
public async Task<WorldSnapshot?> GetWorldAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
{
    if (GetLive(guildId, serverId) is not { } live)
    {
        return null;
    }

    return await live.Connection.GetWorldAsync(_options.HeartbeatTimeout, cancellationToken)
        .ConfigureAwait(false);
}
```

(Copy the exact live-connection-lookup idiom from the neighbouring `GetMapDimensionsAsync` — if it uses a different helper than `GetLive`, mirror it.)

`IRustServerQuery` (after `GetMapDimensionsAsync`):

```csharp
/// <summary>Gets the world size and seed of a connected server, or null when unavailable.</summary>
/// <param name="guildId">The owning guild snowflake.</param>
/// <param name="serverId">The target server id.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>The world snapshot, or null.</returns>
Task<WorldSnapshot?> GetWorldAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
```

`FakeRustSocketSource`: add the member returning a settable default (`Task.FromResult<WorldSnapshot?>(World)` with `public WorldSnapshot? World { get; set; }` — follow the fake's existing property-backed style).

- [ ] **Step 5: Build + run affected test assemblies**

Run:

```bash
dotnet build RustPlusBot.slnx -warnaserror
dotnet test tests/RustPlusBot.Features.Connections.Tests
dotnet test tests/RustPlusBot.Features.Events.Tests
dotnet test tests/RustPlusBot.Features.Map.Tests
dotnet test tests/RustPlusBot.Abstractions.Tests
```

Expected: all PASS (behavioral expectations unchanged by the sweep).

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(map): plumb WorldSize through MapDimensions + GetWorldAsync seam"
```

---

### Task 2: `MapGrid` shared grid math + `GridReference` refactor

**Files:**

- Create: `src/RustPlusBot.Abstractions/Connections/MapGrid.cs`
- Modify: `src/RustPlusBot.Features.Events/Formatting/GridReference.cs`
- Test: `tests/RustPlusBot.Abstractions.Tests/Connections/MapGridTests.cs` (create)
- Test: `tests/RustPlusBot.Features.Events.Tests/Formatting/GridReferenceTests.cs` (extend)

**Interfaces:**

- Consumes: `MapDimensions.WorldSize` (Task 1).
- Produces: `MapGrid.CellSize` (`const float`, 146.25f), `int MapGrid.CellCount(uint worldSize)`, `string MapGrid.ColumnLetters(int index)`, `string MapGrid.LabelFor(float x, float y, uint worldSize)`.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/RustPlusBot.Abstractions.Tests/Connections/MapGridTests.cs
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Tests.Connections;

public sealed class MapGridTests
{
    [Theory]
    [InlineData(3000u, 21)]  // 3000 / 146.25 = 20.51 -> 21 (partial edge cell counts)
    [InlineData(3500u, 24)]  // 23.93 -> 24
    [InlineData(4250u, 30)]  // 29.06 -> 30
    [InlineData(4500u, 31)]  // 30.77 -> 31
    public void CellCount_ceils_partial_edge_cells(uint worldSize, int expected) =>
        Assert.Equal(expected, MapGrid.CellCount(worldSize));

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    public void ColumnLetters_is_spreadsheet_style(int index, string expected) =>
        Assert.Equal(expected, MapGrid.ColumnLetters(index));

    [Fact]
    public void LabelFor_origin_is_bottom_left_last_row()
    {
        // 4000 world -> 28 cells (27.35 ceil). World (0,0) = SW corner = column A, bottom row 27.
        Assert.Equal("A27", MapGrid.LabelFor(0f, 0f, 4000u));
    }

    [Fact]
    public void LabelFor_north_west_corner_is_A0()
    {
        Assert.Equal("A0", MapGrid.LabelFor(0f, 3999f, 4000u));
    }

    [Fact]
    public void LabelFor_beyond_world_size_clamps_to_last_cell()
    {
        // Regression for the old GridReference bug: clamping against IMAGE pixels, not world units.
        Assert.Equal("AB27", MapGrid.LabelFor(4500f, 0f, 4000u)); // col 27 = "AB"
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Abstractions.Tests --filter MapGridTests`
Expected: FAIL — `MapGrid` does not exist.

- [ ] **Step 3: Implement `MapGrid`**

```csharp
// src/RustPlusBot.Abstractions/Connections/MapGrid.cs
using System.Globalization;
using System.Text;

namespace RustPlusBot.Abstractions.Connections;

/// <summary>
/// Rust map grid math shared by the map renderer and grid-reference formatting.
/// One cell is 146.25 game units (rustplusplus-compatible); rows are numbered from the top.
/// </summary>
public static class MapGrid
{
    /// <summary>Edge length of one grid cell, in game units.</summary>
    public const float CellSize = 146.25f;

    /// <summary>Number of grid cells per axis, including a partial edge cell.</summary>
    /// <param name="worldSize">The world size in game units.</param>
    /// <returns>The cell count (at least 1).</returns>
    public static int CellCount(uint worldSize) => Math.Max(1, (int)Math.Ceiling(worldSize / CellSize));

    /// <summary>Formats a spreadsheet-style column label (0→A … 25→Z, 26→AA …).</summary>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>The column letters.</returns>
    public static string ColumnLetters(int index)
    {
        var sb = new StringBuilder();
        var n = index;
        do
        {
            sb.Insert(0, (char)('A' + (n % 26)));
            n = (n / 26) - 1;
        } while (n >= 0);

        return sb.ToString();
    }

    /// <summary>Formats the grid label ("D7") for a world coordinate.</summary>
    /// <param name="x">World X (west→east), clamped into the world.</param>
    /// <param name="y">World Y (south→north), clamped into the world.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <returns>The grid label, rows numbered from the top.</returns>
    public static string LabelFor(float x, float y, uint worldSize)
    {
        var cells = CellCount(worldSize);
        var col = Math.Clamp((int)Math.Floor(Math.Clamp(x, 0f, worldSize - 1) / CellSize), 0, cells - 1);
        var rowFromBottom = Math.Clamp((int)Math.Floor(Math.Clamp(y, 0f, worldSize - 1) / CellSize), 0, cells - 1);
        var row = cells - rowFromBottom - 1;
        return string.Create(CultureInfo.InvariantCulture, $"{ColumnLetters(col)}{row}");
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/RustPlusBot.Abstractions.Tests --filter MapGridTests`
Expected: PASS.

- [ ] **Step 5: Refactor `GridReference` onto `MapGrid`**

Replace the body (keep the public signature and the null fallback; drop the private `GridDiameter` and `ColumnLetters`):

```csharp
using System.Globalization;
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Events.Formatting;

/// <summary>Converts world coordinates to a Rust map grid reference (e.g. "D7"), rustplusplus-compatible.</summary>
public static class GridReference
{
    /// <summary>Formats a grid reference, or raw rounded coordinates when <paramref name="dims"/> is null.</summary>
    /// <param name="x">World X coordinate.</param>
    /// <param name="y">World Y coordinate.</param>
    /// <param name="dims">Map dimensions, or null when unavailable.</param>
    /// <returns>A grid reference like "D7", or "(x, y)" when dimensions are unavailable.</returns>
    public static string From(float x, float y, MapDimensions? dims)
    {
        if (dims is null || dims.WorldSize == 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"({Math.Round(x)}, {Math.Round(y)})");
        }

        return MapGrid.LabelFor(x, y, dims.WorldSize);
    }
}
```

Extend `GridReferenceTests` with the regression case:

```csharp
[Fact]
public void From_uses_world_size_not_image_pixels()
{
    // Image is 2000px but the world is 4000 units: coords beyond 2000 must still resolve.
    var dims = new MapDimensions(2000, 2000, 500, WorldSize: 4000);
    Assert.Equal(GridReference.From(3900f, 3900f, dims), MapGrid.LabelFor(3900f, 3900f, 4000u));
}
```

Note: existing `GridReferenceTests` expectations keep passing because Task 1 set `WorldSize` = old `Width` in every test fixture. If any existing expectation moves by one cell (old code used `Ceiling` on height only), update the expectation to the `MapGrid` value and note it in the commit message — `MapGrid` is the canonical math now.

- [ ] **Step 6: Run tests + commit**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests` and `dotnet test tests/RustPlusBot.Abstractions.Tests`
Expected: PASS.

```bash
git add src tests
git commit -m "feat(map): shared MapGrid math; GridReference uses real world size"
```

---

### Task 3: `MapProjection` (canonical transform)

**Files:**

- Create: `src/RustPlusBot.Features.Map/Rendering/MapProjection.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/MapProjectionTests.cs` (create)

**Interfaces:**

- Produces: `MapProjection(uint WorldSize, int ImageWidth, int ImageHeight, int OceanMarginPx, int OutputSize)` record with `(float X, float Y) ToPixel(float worldX, float worldY)` returning **output-space** pixels (origin top-left, Y down). Consumed by Tasks 5, 8, 9, 11.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/RustPlusBot.Features.Map.Tests/MapProjectionTests.cs
using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapProjectionTests
{
    [Fact]
    public void Rust_plus_style_margin_projects_origin_inside_margin()
    {
        // 4000-unit world on a 2000px image with 100px margin, output 1000px.
        // Playable spans [100, 1900]px on the image -> world(0,0) at image (100, 1900) -> output (50, 950).
        var p = new MapProjection(WorldSize: 4000, ImageWidth: 2000, ImageHeight: 2000, OceanMarginPx: 100, OutputSize: 1000);

        var (x, y) = p.ToPixel(0f, 0f);

        Assert.Equal(50f, x, precision: 3);
        Assert.Equal(950f, y, precision: 3);
    }

    [Fact]
    public void Center_of_world_is_center_of_output()
    {
        var p = new MapProjection(4000, 2000, 2000, 100, 1000);

        var (x, y) = p.ToPixel(2000f, 2000f);

        Assert.Equal(500f, x, precision: 3);
        Assert.Equal(500f, y, precision: 3);
    }

    [Fact]
    public void Rustmaps_style_zero_margin_maps_world_to_full_image()
    {
        // RustMaps raw render: no ocean margin. World (0,0) -> output bottom-left corner.
        var p = new MapProjection(3500, 1750, 1750, 0, 1024);

        var (x0, y0) = p.ToPixel(0f, 0f);
        var (x1, y1) = p.ToPixel(3500f, 3500f);

        Assert.Equal(0f, x0, precision: 3);
        Assert.Equal(1024f, y0, precision: 3);
        Assert.Equal(1024f, x1, precision: 3);
        Assert.Equal(0f, y1, precision: 3);
    }

    [Fact]
    public void North_is_visually_above_south()
    {
        var p = new MapProjection(4000, 2000, 2000, 100, 1000);

        var (_, southY) = p.ToPixel(2000f, 0f);
        var (_, northY) = p.ToPixel(2000f, 4000f);

        Assert.True(northY < southY);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapProjectionTests`
Expected: FAIL — `MapProjection` does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/RustPlusBot.Features.Map/Rendering/MapProjection.cs
namespace RustPlusBot.Features.Map.Rendering;

/// <summary>
/// Projects world coordinates onto the rendered output image.
/// The canonical Rust+ transform: the playable world spans
/// [OceanMarginPx, ImageWidth - OceanMarginPx] on the base image, then the base image is
/// stretched to the square output. Matches the official app and rustplusplus.
/// </summary>
/// <param name="WorldSize">Size of the playable world, in game units.</param>
/// <param name="ImageWidth">Base map image width, in pixels.</param>
/// <param name="ImageHeight">Base map image height, in pixels.</param>
/// <param name="OceanMarginPx">Ocean border baked into the base image, in pixels per side.</param>
/// <param name="OutputSize">Output image edge length, in pixels.</param>
public sealed record MapProjection(uint WorldSize, int ImageWidth, int ImageHeight, int OceanMarginPx, int OutputSize)
{
    /// <summary>Converts a world (x, y) to an output pixel (x, y); origin top-left, Y down.</summary>
    /// <param name="worldX">World X (west→east).</param>
    /// <param name="worldY">World Y (south→north).</param>
    /// <returns>The output-space pixel coordinate.</returns>
    public (float X, float Y) ToPixel(float worldX, float worldY)
    {
        var imgX = (worldX * ((ImageWidth - (2f * OceanMarginPx)) / WorldSize)) + OceanMarginPx;
        var imgY = ImageHeight - ((worldY * ((ImageHeight - (2f * OceanMarginPx)) / WorldSize)) + OceanMarginPx);
        return (imgX * ((float)OutputSize / ImageWidth), imgY * ((float)OutputSize / ImageHeight));
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapProjectionTests`
Expected: PASS. (Hand-check the first test: playable px = 2000−200 = 1800; world 0 → 100px img → ×0.5 → 50 output ✓; Y: 2000−(0+100) = 1900 → 950 ✓.)

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(map): MapProjection canonical world-to-pixel transform"
```

---

### Task 4: `MapRenderStyle` + sized icon accessors in `MapIcons`

**Files:**

- Create: `src/RustPlusBot.Features.Map/Rendering/MapRenderStyle.cs`
- Modify: `src/RustPlusBot.Features.Map/Assets/MapIcons.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/MapIconsTests.cs` (extend)

**Interfaces:**

- Produces: `MapRenderStyle` constants — `MonumentIconSize = 30`, `EventIconSize = 36`, `CargoIconSize = 40`, `PlayerIconSize = 20`, `TrailWidth = 2f`, `GridLabelFontSize = 10f`, `Color TrailColor(MarkerKind kind)`, `int MarkerIconSize(MarkerKind kind)`. Sized accessors — `MapIcons.Marker(MarkerKind kind, int size)`, `MapIcons.Monument(string token, int size)`, `MapIcons.Rig(RigKind kind, bool active, int size)`, `MapIcons.Player(int size)` — each returns an aspect-preserving copy fitting a `size`×`size` box, cached per (key, size). Existing unsized accessors remain.

- [ ] **Step 1: Write failing tests**

Add to `MapIconsTests.cs`:

```csharp
[Fact]
public void Sized_marker_icon_fits_the_requested_box()
{
    var icon = MapIcons.Marker(MarkerKind.CargoShip, 40);

    Assert.NotNull(icon);
    Assert.True(icon!.Width <= 40 && icon.Height <= 40);
    Assert.True(icon.Width == 40 || icon.Height == 40); // aspect-preserving fit, longest edge = size
}

[Fact]
public void Sized_monument_icon_is_scaled_down_from_native()
{
    var native = MapIcons.Monument("oilrig_1");   // 875x875 native
    var sized = MapIcons.Monument("oilrig_1", 30);

    Assert.NotNull(native);
    Assert.NotNull(sized);
    Assert.True(sized!.Width <= 30 && sized.Height <= 30);
}

[Fact]
public void Sized_icons_are_cached_per_size()
{
    Assert.Same(MapIcons.Player(20), MapIcons.Player(20));
    Assert.NotSame(MapIcons.Player(20), MapIcons.Player(24));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapIconsTests`
Expected: FAIL — no overloads taking `int size`.

- [ ] **Step 3: Implement sized accessors**

Add to `MapIcons` (the string keys mirror the existing unsized arms):

```csharp
/// <summary>Gets the marker icon scaled to fit a square box, or null when the kind has no icon.</summary>
/// <param name="kind">The marker kind.</param>
/// <param name="size">The box edge length in pixels; the longest icon edge is scaled to it.</param>
/// <returns>The cached scaled icon, or null.</returns>
public static Image<Rgba32>? Marker(MarkerKind kind, int size) => Scaled(KeyFor(kind), size);

/// <summary>Gets the rig icon scaled to fit a square box, or null when the kind has no icon.</summary>
/// <param name="kind">Which rig.</param>
/// <param name="active">Whether the rig is active (reserved; styling is applied by the renderer).</param>
/// <param name="size">The box edge length in pixels.</param>
/// <returns>The cached scaled icon, or null.</returns>
#pragma warning disable RCS1163, IDE0060 // 'active' is part of the API contract; styling is the renderer's job.
public static Image<Rgba32>? Rig(RigKind kind, bool active, int size) =>
    Scaled(kind switch { RigKind.Small => "oilrig", RigKind.Large => "largeoilrig", _ => null }, size);
#pragma warning restore RCS1163, IDE0060

/// <summary>Gets the player icon scaled to fit a square box, or null when the asset is missing.</summary>
/// <param name="size">The box edge length in pixels.</param>
/// <returns>The cached scaled icon, or null.</returns>
public static Image<Rgba32>? Player(int size) => Scaled("player", size);

/// <summary>Gets the monument icon scaled to fit a square box, or null for unmapped tokens.</summary>
/// <param name="token">The Rust+ monument protobuf token.</param>
/// <param name="size">The box edge length in pixels.</param>
/// <returns>The cached scaled icon, or null.</returns>
public static Image<Rgba32>? Monument(string token, int size) => Scaled(MonumentIconMap.IconKeyFor(token), size);

private static string? KeyFor(MarkerKind kind) => kind switch
{
    MarkerKind.CargoShip => "cargo",
    MarkerKind.PatrolHelicopter => "patrol",
    MarkerKind.Chinook => "ch47",
    MarkerKind.TravellingVendor => "vendor",
    _ => null,
};

private static Image<Rgba32>? Scaled(string? key, int size) => key is null
    ? null
    : Cache.GetOrAdd($"{key}@{size}", _ =>
    {
        var native = Load(key);
        return native?.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(size, size),
        }));
    });
```

Refactor the existing unsized `Marker(MarkerKind)` arm bodies to use `KeyFor` (`Load(KeyFor(kind) …)`) so the key mapping lives once. Add `using SixLabors.ImageSharp.Processing;` for `Resize`.

Create `MapRenderStyle`:

```csharp
// src/RustPlusBot.Features.Map/Rendering/MapRenderStyle.cs
using RustPlusBot.Abstractions.Connections;
using SixLabors.ImageSharp;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>All on-map sizing and styling constants, expressed in pixels at the 1024px output.</summary>
public static class MapRenderStyle
{
    /// <summary>Monument icon box edge, in output pixels (~3% of the output edge).</summary>
    public const int MonumentIconSize = 30;

    /// <summary>Default event-marker icon box edge, in output pixels.</summary>
    public const int EventIconSize = 36;

    /// <summary>Cargo-ship icon box edge, in output pixels (slightly larger — it is a big target).</summary>
    public const int CargoIconSize = 40;

    /// <summary>Player icon box edge, in output pixels.</summary>
    public const int PlayerIconSize = 20;

    /// <summary>Trail polyline stroke width, in output pixels.</summary>
    public const float TrailWidth = 2f;

    /// <summary>Grid cell label font size, in points.</summary>
    public const float GridLabelFontSize = 10f;

    /// <summary>Gets the icon box edge for a marker kind.</summary>
    /// <param name="kind">The marker kind.</param>
    /// <returns>The box edge length in output pixels.</returns>
    public static int MarkerIconSize(MarkerKind kind) =>
        kind == MarkerKind.CargoShip ? CargoIconSize : EventIconSize;

    /// <summary>Gets the trail color for a marker kind.</summary>
    /// <param name="kind">The marker kind.</param>
    /// <returns>The base (fully opaque) trail color.</returns>
    public static Color TrailColor(MarkerKind kind) => kind switch
    {
        MarkerKind.CargoShip => Color.ParseHex("4FC3F7"),
        MarkerKind.PatrolHelicopter => Color.ParseHex("EF5350"),
        MarkerKind.Chinook => Color.ParseHex("FFB74D"),
        MarkerKind.TravellingVendor => Color.ParseHex("81C784"),
        _ => Color.White,
    };
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests`
Expected: PASS (new + existing).

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(map): MapRenderStyle constants + size-scaled icon cache"
```

---

### Task 5: Renderer + composer on `MapProjection` with scaled icons and grid labels

This task lands the visible fix: correct positions, sane icon sizes, per-cell grid labels. After it, the Rust+-based render is already correct (dims.Width/Height ARE the JPEG pixel dims).

**Files:**

- Modify: `src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs` (signature + all draw methods)
- Delete: `src/RustPlusBot.Features.Map/Rendering/WorldToPixel.cs`
- Delete: `tests/RustPlusBot.Features.Map.Tests/WorldToPixelTests.cs` (superseded by `MapProjectionTests`)
- Modify: `src/RustPlusBot.Features.Map/Composing/MapComposer.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/MapRendererTests.cs`, `tests/RustPlusBot.Features.Map.Tests/MapComposerTests.cs` (update)

**Interfaces:**

- Consumes: `MapProjection` (Task 3), `MapGrid` (Task 2), `MapRenderStyle` + sized `MapIcons` (Task 4).
- Produces: `byte[] MapRenderer.Render(byte[] baseImage, MapProjection projection, IReadOnlyList<MarkerPlacement> markers, IReadOnlyList<MonumentPlacement> monuments, IReadOnlyList<PlayerPlacement> players, IReadOnlyList<RigPlacement> rigs, MapLayerSet layers)`. `MapRenderer.OutputSize` stays `1024`. Placement records unchanged in this task.

- [ ] **Step 1: Write the failing scaled-icon test**

Replace the body of `MapRendererTests` marker test(s) to use `MapProjection` and add a bounds assertion:

```csharp
[Fact]
public void Monument_icon_is_drawn_scaled_not_native()
{
    var renderer = new MapRenderer();
    var projection = new MapProjection(4000, 2000, 2000, 100, MapRenderer.OutputSize);
    var baseJpeg = SolidJpeg(2000);   // reuse/create the existing solid-color JPEG helper in this file
    var (px, py) = projection.ToPixel(2000f, 2000f);

    var without = renderer.Render(baseJpeg, projection, [], [], [], [],
        new MapLayerSet(false, false, false, false, false, false));
    var with = renderer.Render(baseJpeg, projection, [],
        [new MonumentPlacement("oilrig_1", px, py)], [], [],
        new MapLayerSet(false, false, true, false, false, false));

    var bounds = ChangedPixelBounds(without, with);
    Assert.True(bounds.Width <= MapRenderStyle.MonumentIconSize + 2,
        $"changed area {bounds.Width}px wide — icon not scaled");
    Assert.True(bounds.Height <= MapRenderStyle.MonumentIconSize + 2);
}
```

Add the `ChangedPixelBounds` helper to the test file (decode both PNGs with `Image.Load<Rgba32>`, iterate pixels, track min/max x/y where they differ, return a `Rectangle`). If the existing tests already have a JPEG factory helper, reuse it; otherwise:

```csharp
private static byte[] SolidJpeg(int size)
{
    using var img = new Image<Rgba32>(size, size, new Rgba32(40, 90, 120));
    using var ms = new MemoryStream();
    img.SaveAsJpeg(ms);
    return ms.ToArray();
}

private static Rectangle ChangedPixelBounds(byte[] a, byte[] b)
{
    using var ia = Image.Load<Rgba32>(a);
    using var ib = Image.Load<Rgba32>(b);
    int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
    for (var y = 0; y < ia.Height; y++)
    {
        for (var x = 0; x < ia.Width; x++)
        {
            if (ia[x, y] != ib[x, y])
            {
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
    }

    return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapRendererTests`
Expected: FAIL — `Render` has no `MapProjection` overload (compile error is the failure at this point).

- [ ] **Step 3: Rework `MapRenderer`**

Signature: replace `MapDimensions dims` with `MapProjection projection` (update the XML docs accordingly). Changes:

1. Delete the `GridDiameter`, `PlayerRadius` constants (grid math → `MapGrid`; sizes → `MapRenderStyle`). Keep `OutputSize`, `OutlinePenWidth`, `ActiveRingWidth`, `PlayerLabelOffset`.
2. Font: expose the family for the grid-label font.

```csharp
private static readonly FontFamily Family = LoadFamily();
private static readonly Font Font = Family.CreateFont(12f);
private static readonly Font GridLabelFont = Family.CreateFont(MapRenderStyle.GridLabelFontSize);

private static FontFamily LoadFamily()
{
    var asm = typeof(MapRenderer).Assembly;
    using var stream = asm.GetManifestResourceStream("RustPlusBot.Features.Map.Assets.LiberationSans-Regular.ttf")
                       ?? throw new InvalidOperationException(
                           "Embedded map font 'RustPlusBot.Features.Map.Assets.LiberationSans-Regular.ttf' not found.");
    var collection = new FontCollection();
    return collection.Add(stream, CultureInfo.InvariantCulture);
}
```

1. `DrawGrid` — lines over the world square only + per-cell labels at each cell's top-left:

```csharp
private static void DrawGrid(Image<Rgba32> image, MapProjection projection)
{
    if (projection.WorldSize == 0)
    {
        return;
    }

    var lineColor = Color.FromRgba(255, 255, 255, 80);
    var labelColor = Color.FromRgba(255, 255, 255, 140);
    var cells = MapGrid.CellCount(projection.WorldSize);
    var worldSize = (float)projection.WorldSize;
    var (left, top) = projection.ToPixel(0f, worldSize);
    var (right, bottom) = projection.ToPixel(worldSize, 0f);

    image.Mutate(ctx =>
    {
        for (var i = 0; i <= cells; i++)
        {
            var boundary = Math.Min(i * MapGrid.CellSize, worldSize);
            var (vx, _) = projection.ToPixel(boundary, 0f);
            ctx.DrawLine(lineColor, OutlinePenWidth, new PointF(vx, top), new PointF(vx, bottom));
            var (_, hy) = projection.ToPixel(0f, boundary);
            ctx.DrawLine(lineColor, OutlinePenWidth, new PointF(left, hy), new PointF(right, hy));
        }

        for (var col = 0; col < cells; col++)
        {
            for (var row = 0; row < cells; row++)
            {
                // Label sits just inside each cell's top-left corner (official-app placement).
                var worldX = col * MapGrid.CellSize;
                var worldY = worldSize - (row * MapGrid.CellSize);
                var (lx, ly) = projection.ToPixel(worldX, worldY);
                var label = MapGrid.ColumnLetters(col) + row.ToString(CultureInfo.InvariantCulture);
                ctx.DrawText(new RichTextOptions(GridLabelFont) { Origin = new PointF(lx + 2f, ly + 2f) },
                    label, labelColor);
            }
        }
    });
}
```

1. `DrawMarkers` / `DrawMonuments` / `DrawRigs` / `DrawPlayers` — swap unsized icon lookups for sized ones:

```csharp
var icon = MapIcons.Marker(marker.Kind, MapRenderStyle.MarkerIconSize(marker.Kind));
// monuments:
var icon = MapIcons.Monument(monument.Token, MapRenderStyle.MonumentIconSize);
// rigs:
var icon = MapIcons.Rig(rig.Kind, rig.Active, MapRenderStyle.MonumentIconSize);
// players (in DrawPlayers, before the loop):
var icon = MapIcons.Player(MapRenderStyle.PlayerIconSize);
```

The rig active-ring keeps its existing formula — it now derives from the scaled icon automatically.

1. Player dead/offline styling — draw the icon dimmed instead of the unreachable dot branch:

```csharp
private static void DrawPlayerIcon(IImageProcessingContext ctx, PlayerPlacement player, Image<Rgba32>? icon)
{
    if (icon is null)
    {
        return;
    }

    var isActive = player is { IsAlive: true, IsOnline: true };
    // Dead/offline teammates render dimmed so status reads at a glance (label adds the suffix).
    ctx.DrawImage(icon, CenterAt(player.PixelX, player.PixelY, icon), isActive ? 1f : 0.45f);
}
```

(The `PlayerRadius` dot fallback is deleted; a missing embedded asset is a build defect, not a runtime state.)

- [ ] **Step 4: Update `MapComposer` to build and use the projection**

In `ComposeAsync`, after the dims fetch:

```csharp
var dims = await query.GetMapDimensionsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
if (dims is null || dims.WorldSize == 0)
{
    // Dimensions unavailable: render the base tile only (every overlay needs world→pixel).
    return renderer.Render(baseImage, new MapProjection(0, 1, 1, 0, MapRenderer.OutputSize),
        markers: [], monuments: [], players: [], rigs: [],
        new MapLayerSet(Grid: false, Markers: false, Monuments: false, Vendor: false, Players: false,
            Rigs: false));
}

// NOTE(Task 9): image pixel dims come from MapDimensions for now (correct for the Rust+ JPEG);
// the base-map source chain replaces this with the actual fetched image's dims.
var projection = new MapProjection(dims.WorldSize, (int)dims.Width, (int)dims.Height, dims.OceanMargin,
    MapRenderer.OutputSize);
```

Replace every `WorldToPixel.ToPixel(m.X, m.Y, dims, MapRenderer.OutputSize)` in the four gather methods with `projection.ToPixel(m.X, m.Y)` (pass `projection` down instead of `dims`), and pass `projection` to `renderer.Render`. Delete `WorldToPixel.cs` and `WorldToPixelTests.cs`.

- [ ] **Step 5: Update remaining tests + run the assembly**

`MapRendererTests` / `MapComposerTests`: replace `MapDimensions` arguments with `MapProjection` per the new signatures (composer tests keep stubbing `GetMapDimensionsAsync` — give the stub `new MapDimensions(2000, 2000, 100, 4000)`).

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests`
Expected: PASS, including `Monument_icon_is_drawn_scaled_not_native`.

- [ ] **Step 6: Build clean + commit**

Run: `dotnet build RustPlusBot.slnx -warnaserror`
Expected: 0 warnings, 0 errors.

```bash
git add -A src tests
git commit -m "fix(map): correct projection, scaled icons, per-cell grid labels"
```

---

### Task 6: `Moved` delta bucket + `MapMarkerSnapshot.Rotation`

**Files:**

- Modify: `src/RustPlusBot.Abstractions/Connections/MapMarkerSnapshot.cs`
- Modify: `src/RustPlusBot.Abstractions/Events/MapMarkersChangedEvent.cs`
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (`PublishMarkerDeltaAsync`, lines 655-670)
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` (`AddMarkers`, ~line 659 — rotation stays null, add the beta.4 note)
- Modify (sweep): every `new MapMarkersChangedEvent(` call site (tests build deltas with 5 args; the compiler will list them)
- Test: `tests/RustPlusBot.Features.Connections.Tests/ConnectionSupervisorTests.cs` (extend)

**Interfaces:**

- Produces: `MapMarkerSnapshot(ulong Id, MarkerKind Kind, float X, float Y, string? Name, float? Rotation = null)`. `MapMarkersChangedEvent(ulong GuildId, Guid ServerId, MapDimensions? Dimensions, IReadOnlyList<MapMarkerSnapshot> Added, IReadOnlyList<MapMarkerSnapshot> Removed, IReadOnlyList<MapMarkerSnapshot> Moved)`. Consumed by Task 7.

- [ ] **Step 1: Extend the records**

```csharp
// MapMarkerSnapshot.cs — Rotation defaults to null so existing call sites stay valid and
// "no heading" is distinguishable from "heading north" once RustPlusApi beta.4 supplies values.
/// <summary>A live map marker observed in one poll.</summary>
/// <param name="Id">The marker id (stable across polls).</param>
/// <param name="Kind">The marker kind.</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
/// <param name="Name">The marker display name, when the game provides one.</param>
/// <param name="Rotation">Heading in degrees as sent by the server, or null when unavailable.</param>
public sealed record MapMarkerSnapshot(ulong Id, MarkerKind Kind, float X, float Y, string? Name, float? Rotation = null);
```

```csharp
// MapMarkersChangedEvent.cs — add Moved as the last positional param + doc line:
/// <param name="Moved">Markers present in both polls whose position or rotation changed.</param>
public sealed record MapMarkersChangedEvent(
    ulong GuildId,
    Guid ServerId,
    MapDimensions? Dimensions,
    IReadOnlyList<MapMarkerSnapshot> Added,
    IReadOnlyList<MapMarkerSnapshot> Removed,
    IReadOnlyList<MapMarkerSnapshot> Moved);
```

- [ ] **Step 2: Build to find broken constructors, sweep tests**

Run: `dotnet build RustPlusBot.slnx -warnaserror 2>&1 | grep error | head -30`
Sweep every 5-arg `new MapMarkersChangedEvent(...)` to pass `Moved: []` (test helpers like `EventStateStoreTests.Delta(...)` get the extra `[]`).

- [ ] **Step 3: Write the failing supervisor test**

Model it on `Marker_added_on_a_later_poll_publishes_changed_event` (`ConnectionSupervisorTests.cs:380-436`) — same harness, subscription loop, and script-before-connect arrangement; only the marker script and assertions differ:

```csharp
[Fact]
public async Task Marker_position_change_publishes_moved_bucket()
{
    // Contract: a marker present in consecutive polls whose position changed lands in Moved
    // (not Added/Removed), so the map can track cargo/heli movement.
    //
    // Script:
    //   Poll 1 → [Cargo id 7 @ (100, 100)]  (baseline — no event)
    //   Poll 2 → [Cargo id 7 @ (150, 130)]  (moved → one event)
    var source = new FakeRustSocketSource();
    source.EnqueueConnect(SocketConnectOutcome.Connected);
    source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
    await using var h = CreateHarness(source);
    var (serverId, _, _) = await SeedAsync(h.Provider);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var captured = new System.Collections.Concurrent.ConcurrentQueue<MapMarkersChangedEvent>();
    var subTask = Task.Run(async () =>
    {
        await foreach (var e in h.Bus.SubscribeAsync<MapMarkersChangedEvent>(cts.Token))
        {
            captured.Enqueue(e);
        }
    }, CancellationToken.None);

    source.EnqueueMarkers([new MapMarkerSnapshot(7UL, MarkerKind.CargoShip, 100f, 100f, "Cargo A")]);
    source.EnqueueMarkers([new MapMarkerSnapshot(7UL, MarkerKind.CargoShip, 150f, 130f, "Cargo A")]);

    await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
    await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);

    Assert.Single(captured);
    Assert.True(captured.TryPeek(out var evt));
    Assert.NotNull(evt);
    Assert.Empty(evt!.Added);
    Assert.Empty(evt.Removed);
    var moved = Assert.Single(evt.Moved);
    Assert.Equal(7UL, moved.Id);
    Assert.Equal(150f, moved.X);
    Assert.Equal(130f, moved.Y);

    await h.Supervisor.StopAllAsync();
    await cts.CancelAsync();
    try
    {
        await subTask;
    }
    catch (OperationCanceledException)
    {
        /* expected */
    }
}
```

Note: with a hold-last marker script (see `Failed_marker_poll_retains_previous_snapshot`), identical consecutive polls must NOT publish — the existing no-delta expectations already cover this and must stay green after the `Moved` change.

- [ ] **Step 4: Run to verify failure, then implement**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter Marker_position_change_publishes_moved_bucket`
Expected: FAIL (no `Moved` bucket published).

Implement in `PublishMarkerDeltaAsync`:

```csharp
private async Task PublishMarkerDeltaAsync(
    (ulong Guild, Guid Server) key,
    MapDimensions? dims,
    IReadOnlyList<MapMarkerSnapshot> previous,
    IReadOnlyList<MapMarkerSnapshot> current,
    CancellationToken ct)
{
    var previousById = previous.ToDictionary(p => p.Id);
    var added = new List<MapMarkerSnapshot>();
    var moved = new List<MapMarkerSnapshot>();
    foreach (var c in current)
    {
        if (!previousById.TryGetValue(c.Id, out var p))
        {
            added.Add(c);
        }
        else if (c.X != p.X || c.Y != p.Y || !Nullable.Equals(c.Rotation, p.Rotation))
        {
            // Exact float compare is intentional: a stationary marker round-trips identical floats.
            moved.Add(c);
        }
    }

    var removed = previous.Where(p => current.All(c => c.Id != p.Id)).ToList();
    if (added.Count > 0 || removed.Count > 0 || moved.Count > 0)
    {
        await eventBus.PublishAsync(
                new MapMarkersChangedEvent(key.Guild, key.Server, dims, added, removed, moved), ct)
            .ConfigureAwait(false);
    }
}
```

In `RustPlusSocketSource.AddMarkers` (~line 659-676), keep `Rotation` unset but leave the tracking note where `Name: null` is set:

```csharp
// Rotation: RustPlusApi 2.0.0-beta.3 does not map AppMarker.rotation; wire it here once
// 2.0.0-beta.4 ships (see RustPlusApi docs/development/beta4-map-marker-rotation.md).
```

- [ ] **Step 5: Run affected assemblies**

Run:

```bash
dotnet test tests/RustPlusBot.Features.Connections.Tests
dotnet test tests/RustPlusBot.Features.Events.Tests
dotnet test tests/RustPlusBot.Features.Players.Tests
dotnet test tests/RustPlusBot.Features.Commands.Tests
dotnet test tests/RustPlusBot.Abstractions.Tests
```

Expected: PASS. (`MarkerEventClassifier` iterates only `Added`/`Removed`, `EventRelay.RelayAsync` applies the store then early-returns on zero classified events — Moved-only deltas update state without posting; no code change needed in either.)

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(events): Moved bucket in marker deltas + nullable marker rotation"
```

---

### Task 7: `EventStateStore` position updates + history ring

**Files:**

- Modify: `src/RustPlusBot.Features.Events/State/ActiveMarker.cs`
- Create: `src/RustPlusBot.Features.Events/State/TrailPoint.cs`
- Modify: `src/RustPlusBot.Features.Events/State/EventStateStore.cs` (`Apply`, lines 52-80)
- Test: `tests/RustPlusBot.Features.Events.Tests/State/EventStateStoreTests.cs` (extend)

**Interfaces:**

- Consumes: `MapMarkersChangedEvent.Moved`, `MapMarkerSnapshot.Rotation` (Task 6).
- Produces: `TrailPoint(float X, float Y)`. `ActiveMarker(ulong Id, MarkerKind Kind, float X, float Y, MapDimensions? Dimensions, DateTimeOffset SeenAtUtc, IReadOnlyList<TrailPoint> History, float? Rotation)` — `History` is oldest-first, newest last, max 6 entries, always contains the current position as its last entry. Consumed by Task 8's composer.

- [ ] **Step 1: Write failing tests**

Add to `EventStateStoreTests` (the `Delta` helpers gained a `Moved` arg in Task 6 — add a `DeltaMoved` helper):

```csharp
private static MapMarkersChangedEvent DeltaMoved(IReadOnlyList<MapMarkerSnapshot> moved) =>
    new(Guild, Server, null, [], [], moved);

[Fact]
public void Moved_marker_updates_position_and_rotation()
{
    var store = Build();
    store.Apply(Delta([new MapMarkerSnapshot(1, MarkerKind.CargoShip, 10f, 20f, null)], []), []);

    store.Apply(DeltaMoved([new MapMarkerSnapshot(1, MarkerKind.CargoShip, 30f, 40f, null, Rotation: 90f)]), []);

    var active = Assert.Single(store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip));
    Assert.Equal(30f, active.X);
    Assert.Equal(40f, active.Y);
    Assert.Equal(90f, active.Rotation);
}

[Fact]
public void History_ring_appends_and_caps_at_six()
{
    var store = Build();
    store.Apply(Delta([new MapMarkerSnapshot(1, MarkerKind.CargoShip, 0f, 0f, null)], []), []);

    for (var i = 1; i <= 8; i++)
    {
        store.Apply(DeltaMoved([new MapMarkerSnapshot(1, MarkerKind.CargoShip, i * 10f, 0f, null)]), []);
    }

    var active = Assert.Single(store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip));
    Assert.Equal(6, active.History.Count);
    Assert.Equal(80f, active.History[^1].X);  // newest last = current position
    Assert.Equal(30f, active.History[0].X);   // oldest surviving point
}

[Fact]
public void Moved_for_unknown_id_is_ignored()
{
    var store = Build();

    store.Apply(DeltaMoved([new MapMarkerSnapshot(99, MarkerKind.CargoShip, 1f, 2f, null)]), []);

    Assert.Empty(store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter EventStateStoreTests`
Expected: FAIL — `ActiveMarker` has no `History`/`Rotation`.

- [ ] **Step 3: Implement**

```csharp
// TrailPoint.cs (new)
namespace RustPlusBot.Features.Events.State;

/// <summary>One historical marker position, in world coordinates.</summary>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
public sealed record TrailPoint(float X, float Y);
```

```csharp
// ActiveMarker.cs — extend (History oldest-first, newest last; always ends at the current position):
/// <param name="History">Recent positions, oldest first, newest (current) last; capped.</param>
/// <param name="Rotation">Heading in degrees as sent by the server, or null.</param>
public sealed record ActiveMarker(
    ulong Id,
    MarkerKind Kind,
    float X,
    float Y,
    MapDimensions? Dimensions,
    DateTimeOffset SeenAtUtc,
    IReadOnlyList<TrailPoint> History,
    float? Rotation);
```

`EventStateStore.Apply` — seed on Added, update on Moved (add `private const int HistoryCapacity = 6;`):

```csharp
foreach (var m in delta.Added)
{
    state.Active[m.Id] = new ActiveMarker(m.Id, m.Kind, m.X, m.Y, delta.Dimensions, now,
        [new TrailPoint(m.X, m.Y)], m.Rotation);
}

foreach (var m in delta.Moved)
{
    if (!state.Active.TryGetValue(m.Id, out var existing))
    {
        continue; // moved-before-added can only happen after a Clear race; drop it
    }

    var history = new List<TrailPoint>(existing.History) { new(m.X, m.Y) };
    if (history.Count > HistoryCapacity)
    {
        history.RemoveAt(0);
    }

    state.Active[m.Id] = existing with { X = m.X, Y = m.Y, Rotation = m.Rotation, History = history };
}
```

Fix the two existing `new ActiveMarker(...)` construction sites the compiler flags in tests (append `[new TrailPoint(x, y)], null`).

- [ ] **Step 4: Run tests + commit**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests`
Expected: PASS.

```bash
git add src tests
git commit -m "feat(events): active markers track movement + 6-deep history ring"
```

---

### Task 8: Trails + rotation in composer and renderer

**Files:**

- Modify: `src/RustPlusBot.Features.Map/Rendering/MarkerPlacement.cs`
- Modify: `src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs` (trails layer + rotated icon draw)
- Modify: `src/RustPlusBot.Features.Map/Composing/MapComposer.cs` (`GatherMarkers`)
- Test: `tests/RustPlusBot.Features.Map.Tests/MapRendererTests.cs`, `MapComposerTests.cs` (extend)

**Interfaces:**

- Consumes: `ActiveMarker.History`/`Rotation` (Task 7), `MapRenderStyle.TrailColor`/`TrailWidth` (Task 4).
- Produces: `MarkerPlacement(MarkerKind Kind, float PixelX, float PixelY, float? Rotation, IReadOnlyList<PointF> Trail)` — `Trail` is output-space pixels, oldest first. Draw order: base → grid → monuments → **trails** → markers → rigs → players.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Trail_draws_pixels_between_history_points()
{
    var renderer = new MapRenderer();
    var projection = new MapProjection(4000, 2000, 2000, 100, MapRenderer.OutputSize);
    var baseJpeg = SolidJpeg(2000);
    var (ax, ay) = projection.ToPixel(1000f, 2000f);
    var (bx, by) = projection.ToPixel(2000f, 2000f);
    var layers = new MapLayerSet(false, true, false, false, false, false);

    var without = renderer.Render(baseJpeg, projection,
        [new MarkerPlacement(MarkerKind.CargoShip, bx, by, null, [])], [], [], [], layers);
    var with = renderer.Render(baseJpeg, projection,
        [new MarkerPlacement(MarkerKind.CargoShip, bx, by, null,
            [new PointF(ax, ay), new PointF(bx, by)])], [], [], [], layers);

    var bounds = ChangedPixelBounds(without, with);
    // The trail spans from A to B — far wider than the icon alone.
    Assert.True(bounds.Width > MapRenderStyle.CargoIconSize * 2, $"no trail drawn (width {bounds.Width}px)");
}

[Fact]
public void Rotation_changes_the_rendered_icon()
{
    var renderer = new MapRenderer();
    var projection = new MapProjection(4000, 2000, 2000, 100, MapRenderer.OutputSize);
    var baseJpeg = SolidJpeg(2000);
    var (px, py) = projection.ToPixel(2000f, 2000f);
    var layers = new MapLayerSet(false, true, false, false, false, false);

    var unrotated = renderer.Render(baseJpeg, projection,
        [new MarkerPlacement(MarkerKind.CargoShip, px, py, null, [])], [], [], [], layers);
    var rotated = renderer.Render(baseJpeg, projection,
        [new MarkerPlacement(MarkerKind.CargoShip, px, py, 45f, [])], [], [], [], layers);

    Assert.False(unrotated.AsSpan().SequenceEqual(rotated));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapRendererTests`
Expected: FAIL — `MarkerPlacement` has no `Rotation`/`Trail` (compile error).

- [ ] **Step 3: Implement**

```csharp
// MarkerPlacement.cs
using RustPlusBot.Abstractions.Connections;
using SixLabors.ImageSharp;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>One marker to draw, already projected to pixel coordinates.</summary>
/// <param name="Kind">The marker kind (selects the icon).</param>
/// <param name="PixelX">Pixel X on the rendered tile.</param>
/// <param name="PixelY">Pixel Y on the rendered tile.</param>
/// <param name="Rotation">Heading in degrees as sent by the server, or null for no rotation.</param>
/// <param name="Trail">Recent positions in output pixels, oldest first; empty for no trail.</param>
public sealed record MarkerPlacement(
    MarkerKind Kind,
    float PixelX,
    float PixelY,
    float? Rotation,
    IReadOnlyList<PointF> Trail);
```

`MapRenderer.Render`: insert the trails layer between monuments and markers:

```csharp
if (layers.Markers || layers.Vendor)
{
    DrawTrails(image, markers);
}
```

```csharp
private static void DrawTrails(Image<Rgba32> image, IReadOnlyList<MarkerPlacement> markers)
{
    image.Mutate(ctx =>
    {
        foreach (var marker in markers)
        {
            if (marker.Trail.Count < 2)
            {
                continue;
            }

            var baseColor = MapRenderStyle.TrailColor(marker.Kind);
            for (var i = 1; i < marker.Trail.Count; i++)
            {
                // Fade from faint (oldest) to strong (newest) so travel direction reads instantly.
                var alpha = 0.15f + (0.45f * i / (marker.Trail.Count - 1));
                ctx.DrawLine(baseColor.WithAlpha(alpha), MapRenderStyle.TrailWidth,
                    marker.Trail[i - 1], marker.Trail[i]);
            }
        }
    });
}
```

`DrawMarkers` — rotate when a heading is present (desktop-app convention: negate for the Y-down canvas; per-kind flip corrections, if the visual pass in Task 11 shows any, become constants in `MapRenderStyle`):

```csharp
private static void DrawMarkers(Image<Rgba32> image, IReadOnlyList<MarkerPlacement> markers)
{
    image.Mutate(ctx =>
    {
        foreach (var marker in markers)
        {
            var icon = MapIcons.Marker(marker.Kind, MapRenderStyle.MarkerIconSize(marker.Kind));
            if (icon is null)
            {
                continue;
            }

            if (marker.Rotation is { } rotation && Math.Abs(rotation) > 0.01f)
            {
                using var rotated = icon.Clone(c => c.Rotate(-rotation));
                ctx.DrawImage(rotated, CenterAt(marker.PixelX, marker.PixelY, rotated), 1f);
            }
            else
            {
                ctx.DrawImage(icon, CenterAt(marker.PixelX, marker.PixelY, icon), 1f);
            }
        }
    });
}
```

`MapComposer.GatherMarkers` — project the history ring (both the `LiveMarkerKinds` loop and the vendor loop):

```csharp
foreach (var m in events.GetActiveMarkers(guildId, serverId, kind))
{
    var (px, py) = projection.ToPixel(m.X, m.Y);
    var trail = new List<PointF>(m.History.Count);
    foreach (var h in m.History)
    {
        var (tx, ty) = projection.ToPixel(h.X, h.Y);
        trail.Add(new PointF(tx, ty));
    }

    markers.Add(new MarkerPlacement(kind, px, py, m.Rotation, trail));
}
```

Fix remaining `new MarkerPlacement(...)` compile errors in tests (append `null, []`).

- [ ] **Step 4: Run tests + commit**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests`
Expected: PASS.

```bash
git add src tests
git commit -m "feat(map): fading motion trails + rotation-aware marker drawing"
```

---

### Task 9: Base-map source chain (`BaseMapImage`, sources, cache rework)

**Files:**

- Create: `src/RustPlusBot.Features.Map/Composing/BaseMapImage.cs`
- Create: `src/RustPlusBot.Features.Map/Composing/IBaseMapSource.cs`
- Create: `src/RustPlusBot.Features.Map/Composing/RustPlusBaseMapSource.cs`
- Modify: `src/RustPlusBot.Features.Map/Composing/BaseMapCache.cs`
- Modify: `src/RustPlusBot.Features.Map/Composing/MapComposer.cs` (projection from `BaseMapImage`)
- Modify: `src/RustPlusBot.Features.Map/MapServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/BaseMapCacheTests.cs` (rework), `MapComposerTests.cs`, `MapRegistrationTests.cs` (update)

**Interfaces:**

- Produces: `BaseMapImage(byte[] Bytes, int PixelWidth, int PixelHeight, int OceanMarginPx)`. `interface IBaseMapSource { Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken); }`. `BaseMapCache(IEnumerable<IBaseMapSource> sources)` — tries sources in registration order, caches the first hit; `Task<BaseMapImage?> GetAsync(...)`, `void Clear(...)` unchanged semantics. Consumed by Task 10.

- [ ] **Step 1: Write failing tests**

Rework `BaseMapCacheTests` (drop the `IRustServerQuery` substitute, use fake sources):

```csharp
private sealed class FakeSource(BaseMapImage? result) : IBaseMapSource
{
    public int Calls { get; private set; }

    public Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(result);
    }
}

[Fact]
public async Task First_source_wins_and_is_cached()
{
    var preferred = new FakeSource(new BaseMapImage([1, 2], 100, 100, 0));
    var fallback = new FakeSource(new BaseMapImage([9, 9], 200, 200, 50));
    var cache = new BaseMapCache([preferred, fallback]);
    var server = Guid.NewGuid();

    var first = await cache.GetAsync(1, server, CancellationToken.None);
    var second = await cache.GetAsync(1, server, CancellationToken.None);

    Assert.Equal(100, first!.PixelWidth);
    Assert.Same(first, second);
    Assert.Equal(1, preferred.Calls);   // cached after the first hit
    Assert.Equal(0, fallback.Calls);
}

[Fact]
public async Task Falls_through_to_next_source_when_first_returns_null()
{
    var preferred = new FakeSource(null);
    var fallback = new FakeSource(new BaseMapImage([9], 200, 200, 50));
    var cache = new BaseMapCache([preferred, fallback]);

    var result = await cache.GetAsync(1, Guid.NewGuid(), CancellationToken.None);

    Assert.Equal(200, result!.PixelWidth);
}

[Fact]
public async Task All_null_is_not_cached_and_retries()
{
    var source = new FakeSource(null);
    var cache = new BaseMapCache([source]);
    var server = Guid.NewGuid();

    Assert.Null(await cache.GetAsync(1, server, CancellationToken.None));
    Assert.Null(await cache.GetAsync(1, server, CancellationToken.None));
    Assert.Equal(2, source.Calls);
}
```

Plus a `RustPlusBaseMapSource` test with an `IRustServerQuery` substitute: image bytes + dims present → `BaseMapImage(bytes, (int)Width, (int)Height, OceanMargin)`; either null → null.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter "BaseMapCacheTests|RustPlusBaseMapSourceTests"`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement**

```csharp
// BaseMapImage.cs
namespace RustPlusBot.Features.Map.Composing;

/// <summary>A fetched base-map image plus the projection metadata of that specific image.</summary>
/// <param name="Bytes">The encoded image bytes (JPEG or PNG).</param>
/// <param name="PixelWidth">Image width in pixels.</param>
/// <param name="PixelHeight">Image height in pixels.</param>
/// <param name="OceanMarginPx">Ocean border baked into this image, in pixels per side.</param>
public sealed record BaseMapImage(byte[] Bytes, int PixelWidth, int PixelHeight, int OceanMarginPx);
```

```csharp
// IBaseMapSource.cs
namespace RustPlusBot.Features.Map.Composing;

/// <summary>One provider of the static-per-wipe base map image. Sources are tried in registration order.</summary>
public interface IBaseMapSource
{
    /// <summary>Fetches the base map, or null when this source cannot provide one (falls through).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The base map image, or null.</returns>
    Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
```

```csharp
// RustPlusBaseMapSource.cs
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Map.Composing;

/// <summary>Fallback base-map source: the JPEG tile served by the Rust+ server itself.</summary>
/// <param name="query">The live query seam.</param>
public sealed class RustPlusBaseMapSource(IRustServerQuery query) : IBaseMapSource
{
    /// <inheritdoc />
    public async Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var bytes = await query.GetMapImageAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        var dims = await query.GetMapDimensionsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (dims is null)
        {
            return null;
        }

        return new BaseMapImage(bytes, (int)dims.Width, (int)dims.Height, dims.OceanMargin);
    }
}
```

`BaseMapCache` — replace the `IRustServerQuery` dependency:

```csharp
/// <summary>Caches the static-per-wipe base map per (guild, server), trying sources in order on a miss.</summary>
/// <param name="sources">Base-map sources in priority order (first hit wins).</param>
public sealed class BaseMapCache(IEnumerable<IBaseMapSource> sources)
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), BaseMapImage> _images = new();

    /// <summary>Gets the cached base map, fetching from the source chain on a miss. Null results are not cached.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The base map image, or null if no source can provide one.</returns>
    public async Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        if (_images.TryGetValue((guildId, serverId), out var cached))
        {
            return cached;
        }

        foreach (var source in sources)
        {
            var fetched = await source.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            if (fetched is not null)
            {
                _images[(guildId, serverId)] = fetched;
                return fetched;
            }
        }

        return null;
    }

    /// <summary>Evicts the cached base map for a server (called on disconnect).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    public void Clear(ulong guildId, Guid serverId) => _images.TryRemove((guildId, serverId), out _);
}
```

`MapComposer.ComposeAsync` — the projection now uses the fetched image's own metadata (replace the Task-5 interim `NOTE`):

```csharp
var baseImage = await cache.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
if (baseImage is null)
{
    return null;
}
// ... settings/layers unchanged ...
var dims = await query.GetMapDimensionsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
if (dims is null || dims.WorldSize == 0)
{
    return renderer.Render(baseImage.Bytes, new MapProjection(0, 1, 1, 0, MapRenderer.OutputSize),
        markers: [], monuments: [], players: [], rigs: [],
        new MapLayerSet(Grid: false, Markers: false, Monuments: false, Vendor: false, Players: false,
            Rigs: false));
}

var projection = new MapProjection(dims.WorldSize, baseImage.PixelWidth, baseImage.PixelHeight,
    baseImage.OceanMarginPx, MapRenderer.OutputSize);
```

(`renderer.Render` first arg becomes `baseImage.Bytes` in both branches.)

`MapServiceCollectionExtensions.AddMap`: `services.AddSingleton<IBaseMapSource, RustPlusBaseMapSource>();` before the `BaseMapCache` registration (Task 10 inserts RustMaps ahead of it).

- [ ] **Step 4: Update composer/registration tests, run assembly + commit**

`MapComposerTests`: the cache is constructed with a fake source list instead of an `IRustServerQuery` substitute for map bytes. `MapRegistrationTests`: assert `IBaseMapSource` resolves.

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests`
Expected: PASS.

```bash
git add src tests
git commit -m "feat(map): base-map source chain with per-image projection metadata"
```

---

### Task 10: RustMaps source, options, DI, config

**Files:**

- Modify: `Directory.Packages.props` (add `<PackageVersion Include="RustMapsApi" Version="1.0.0-beta.1" />` in the Packages group)
- Modify: `src/RustPlusBot.Features.Map/RustPlusBot.Features.Map.csproj` (add `<PackageReference Include="RustMapsApi" />`)
- Modify: `src/RustPlusBot.Features.Map/MapOptions.cs`
- Create: `src/RustPlusBot.Features.Map/Composing/RustMapsBaseMapSource.cs`
- Modify: `src/RustPlusBot.Features.Map/MapServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Host/Program.cs` (lines 80-84)
- Modify: `src/RustPlusBot.Host/appsettings.json` (Map section)
- Test: `tests/RustPlusBot.Features.Map.Tests/RustMapsBaseMapSourceTests.cs` (create), `MapRegistrationTests.cs` (extend)

**Interfaces:**

- Consumes: `IRustMapsClient.GetMapBySeedAndSizeAsync(int size, int seed, bool staging, CancellationToken)` → `Result<MapInfo>` (`IsSuccess`, `Data`, `Error`); `MapInfo.RawImageUrl`; `IRustServerQuery.GetWorldAsync` (Task 1); `IBaseMapSource`/`BaseMapImage` (Task 9).
- Produces: `MapOptions.MapRefreshInterval` default 30s; `MapOptions.RustMaps.ApiKey` (`RustMapsOptions`); `AddMap(IServiceCollection, IConfiguration)`; named HTTP client `RustMapsBaseMapSource.HttpClientName`.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/RustPlusBot.Features.Map.Tests/RustMapsBaseMapSourceTests.cs
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustMapsApi.Results;
using RustMapsApi.V4;
using RustMapsApi.V4.Models;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Composing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RustPlusBot.Features.Map.Tests;

public sealed class RustMapsBaseMapSourceTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();

    private sealed class StubHandler(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static byte[] Png(int size)
    {
        using var img = new Image<Rgba32>(size, size);
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static IRustServerQuery QueryWithWorld(WorldSnapshot? world)
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetWorldAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(world);
        return query;
    }

    [Fact]
    public async Task Happy_path_downloads_raw_image_and_measures_dims()
    {
        var client = Substitute.For<IRustMapsClient>();
        client.GetMapBySeedAndSizeAsync(3500, 1234, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Success(new MapInfo { RawImageUrl = "https://img.example/raw.png" }, 200));
        var source = new RustMapsBaseMapSource(client, QueryWithWorld(new WorldSnapshot(3500, 1234)),
            new StubFactory(new StubHandler(HttpStatusCode.OK, Png(1750))), NullLogger<RustMapsBaseMapSource>.Instance);

        var result = await source.GetAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1750, result!.PixelWidth);
        Assert.Equal(0, result.OceanMarginPx);
    }

    [Fact]
    public async Task Map_not_found_returns_null()
    {
        var client = Substitute.For<IRustMapsClient>();
        client.GetMapBySeedAndSizeAsync(3500, 1234, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Failure(
                new RustMapsError(RustMapsErrorKind.NotFound, "not generated", RawBody: null, RetryAfter: null), 404));
        var source = new RustMapsBaseMapSource(client, QueryWithWorld(new WorldSnapshot(3500, 1234)),
            new StubFactory(new StubHandler(HttpStatusCode.OK, Png(16))), NullLogger<RustMapsBaseMapSource>.Instance);

        Assert.Null(await source.GetAsync(Guild, Server, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_world_info_returns_null()
    {
        var client = Substitute.For<IRustMapsClient>();
        var source = new RustMapsBaseMapSource(client, QueryWithWorld(null),
            new StubFactory(new StubHandler(HttpStatusCode.OK, Png(16))), NullLogger<RustMapsBaseMapSource>.Instance);

        Assert.Null(await source.GetAsync(Guild, Server, CancellationToken.None));
        await client.DidNotReceiveWithAnyArgs().GetMapBySeedAndSizeAsync(0, 0, false, CancellationToken.None);
    }
}
```

(`RustMapsError` is `sealed record RustMapsError(RustMapsErrorKind Kind, string? Message, string? RawBody, TimeSpan? RetryAfter)` — verified against RustMapsApi `1.0.0-beta.1` sources.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter RustMapsBaseMapSourceTests`
Expected: FAIL — type/package missing. Add the package entries first if the compile fails on the `using RustMapsApi...` lines.

- [ ] **Step 3: Implement**

```csharp
// RustMapsBaseMapSource.cs
using Microsoft.Extensions.Logging;
using RustMapsApi.V4;
using RustPlusBot.Abstractions.Connections;
using SixLabors.ImageSharp;

namespace RustPlusBot.Features.Map.Composing;

/// <summary>
/// Preferred base-map source: the RustMaps procedural render (clean terrain, no baked icons),
/// resolved by the server's world size + seed. Any failure returns null so the chain falls
/// through to the Rust+ JPEG — custom/unpublished maps must still render.
/// </summary>
/// <param name="client">The RustMaps API client.</param>
/// <param name="query">The live query seam (world size + seed).</param>
/// <param name="httpClientFactory">Creates the image-download client.</param>
/// <param name="logger">Logs fall-through causes.</param>
public sealed partial class RustMapsBaseMapSource(
    IRustMapsClient client,
    IRustServerQuery query,
    IHttpClientFactory httpClientFactory,
    ILogger<RustMapsBaseMapSource> logger) : IBaseMapSource
{
    /// <summary>Named HTTP client used to download the rendered image.</summary>
    public const string HttpClientName = "RustMapsImages";

    /// <inheritdoc />
    public async Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        try
        {
            var world = await query.GetWorldAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            if (world is null)
            {
                return null;
            }

            var result = await client.GetMapBySeedAndSizeAsync(
                    (int)world.WorldSize, (int)world.Seed, staging: false, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess || result.Data?.RawImageUrl is not { } imageUrl)
            {
                LogRustMapsUnavailable(logger, (int)world.WorldSize, (int)world.Seed, result.StatusCode);
                return null;
            }

            var http = httpClientFactory.CreateClient(HttpClientName);
            var bytes = await http.GetByteArrayAsync(new Uri(imageUrl), cancellationToken).ConfigureAwait(false);
            var info = Image.Identify(bytes);
            // RustMaps raw renders span the playable world edge-to-edge (no ocean border).
            // VERIFY on the first live fetch via tools/RustPlusBot.MapParity; adjust here if wrong.
            return new BaseMapImage(bytes, info.Width, info.Height, OceanMarginPx: 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: any RustMaps failure falls through to the Rust+ JPEG source.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRustMapsFailed(logger, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "RustMaps has no render for size {Size} seed {Seed} (status {StatusCode}); falling back to the Rust+ map tile.")]
    private static partial void LogRustMapsUnavailable(ILogger logger, int size, int seed, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RustMaps base-map fetch failed; falling back to the Rust+ map tile.")]
    private static partial void LogRustMapsFailed(ILogger logger, Exception exception);
}
```

(Match the repo's existing `LoggerMessage` usage style, e.g. in `RustPlusSocketSource`.)

```csharp
// MapOptions.cs
namespace RustPlusBot.Features.Map;

/// <summary>Map feature configuration, bound from the "Map" config section.</summary>
public sealed class MapOptions
{
    /// <summary>Minimum time between #map image re-renders per server (coalesces rapid marker changes). Default 30s.</summary>
    public TimeSpan MapRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>RustMaps integration settings.</summary>
    public RustMapsOptions RustMaps { get; set; } = new();
}

/// <summary>RustMaps API settings; the integration is inactive when no key is configured.</summary>
public sealed class RustMapsOptions
{
    /// <summary>The RustMaps API key, or null/empty to disable the RustMaps base-map source.</summary>
    public string? ApiKey { get; set; }
}
```

`MapServiceCollectionExtensions` — new signature reading the key at registration time:

```csharp
using Microsoft.Extensions.Configuration;
// ...

/// <summary>Registers the renderer, base-map source chain, composer, poster, pipeline bundle, and hosted service.</summary>
/// <param name="services">The service collection to add to.</param>
/// <param name="configuration">The host configuration (reads Map:RustMaps:ApiKey).</param>
/// <returns>The same service collection, for chaining.</returns>
public static IServiceCollection AddMap(this IServiceCollection services, IConfiguration configuration)
{
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(configuration);

    services.AddSingleton<MapRenderer>();

    // Source order defines priority: RustMaps first (when a key is configured), Rust+ JPEG fallback.
    var rustMapsKey = configuration["Map:RustMaps:ApiKey"];
    if (!string.IsNullOrWhiteSpace(rustMapsKey))
    {
        services.AddRustMapsClientV4(o => o.ApiKey = rustMapsKey);
        services.AddHttpClient(RustMapsBaseMapSource.HttpClientName);
        services.AddSingleton<IBaseMapSource, RustMapsBaseMapSource>();
    }

    services.AddSingleton<IBaseMapSource, RustPlusBaseMapSource>();
    services.AddSingleton<BaseMapCache>();
    services.AddSingleton<MapComposer>();
    services.AddSingleton<IMapChannelPoster, DiscordMapChannelPoster>();
    services.AddSingleton<MapPipeline>();
    services.AddHostedService<MapHostedService>();

    return services;
}
```

`Program.cs` line 84: `builder.Services.AddMap(builder.Configuration);`

`appsettings.json`:

```json
"Map": {
  "MapRefreshInterval": "00:00:30",
  "RustMaps": {
    "ApiKey": ""
  }
}
```

`MapRegistrationTests`: extend with a with-key case (build a `ConfigurationBuilder().AddInMemoryCollection(...)` containing `Map:RustMaps:ApiKey = "test-key"`) asserting two `IBaseMapSource` registrations resolve with `RustMapsBaseMapSource` first, and a no-key case asserting only `RustPlusBaseMapSource`.

- [ ] **Step 4: Run tests + build + commit**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests` then `dotnet build RustPlusBot.slnx -warnaserror`
Expected: PASS / clean.

```bash
git add -A src tests Directory.Packages.props
git commit -m "feat(map): RustMaps preferred base-map source behind optional API key; 30s refresh"
```

---

### Task 11: Ground-truth parity harness (`tools/RustPlusBot.MapParity`) + fixture test

**Files:**

- Create: `tools/RustPlusBot.MapParity/RustPlusBot.MapParity.csproj`
- Create: `tools/RustPlusBot.MapParity/Program.cs`
- Create: `tools/RustPlusBot.MapParity/README.md`
- Create: `tests/RustPlusBot.Features.Map.Tests/Fixtures/` (fixture JSON lands here when generated)
- Create: `tests/RustPlusBot.Features.Map.Tests/RustMapsParityTests.cs`
- Modify: `tests/RustPlusBot.Features.Map.Tests/RustPlusBot.Features.Map.Tests.csproj` (add `Xunit.SkippableFact` + fixture content copy)

**Interfaces:**

- Consumes: `MapProjection` (Task 3), `MapGrid` (Task 2), `IRustMapsClient` (Task 10).
- Produces: a manual CLI (`dotnet run --project tools/RustPlusBot.MapParity -- <size> <seed> <apiKey> <outDir>`) that downloads the RustMaps `ImageUrl` render + monument list, projects each monument through `MapProjection`, draws crosshairs onto a copy, and writes `overlay.png` + `monuments.json` for eyeball diffing. Plus a skippable parity test reading the committed fixture.

- [ ] **Step 1: Create the tool** (mirror `tools/RustPlusBot.ItemData.Generator`'s csproj shape — `net10.0`, `OutputType=Exe`, not in the solution's src group; add it to `RustPlusBot.slnx` the same way the generator is included):

```xml
<!-- tools/RustPlusBot.MapParity/RustPlusBot.MapParity.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="RustMapsApi" />
    <PackageReference Include="SixLabors.ImageSharp" />
    <PackageReference Include="SixLabors.ImageSharp.Drawing" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\RustPlusBot.Features.Map\RustPlusBot.Features.Map.csproj" />
  </ItemGroup>
</Project>
```

```csharp
// tools/RustPlusBot.MapParity/Program.cs
using System.Text.Json;
using RustMapsApi.V4;
using RustMapsApi;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: map-parity <worldSize> <seed> <apiKey> <outDir>");
    return 1;
}

var size = int.Parse(args[0]);
var seed = int.Parse(args[1]);
var apiKey = args[2];
var outDir = Directory.CreateDirectory(args[3]).FullName;

using var http = new HttpClient { BaseAddress = new Uri("https://api.rustmaps.com") };
http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
var client = new RustMapsClient(http); // verified: RustMapsClient has a single HttpClient primary ctor

var result = await client.GetMapBySeedAndSizeAsync(size, seed, staging: false);
if (!result.IsSuccess || result.Data is not { } map)
{
    Console.Error.WriteLine($"RustMaps lookup failed (status {result.StatusCode}): {result.Error?.Message}");
    return 2;
}

Console.WriteLine($"Map {map.Id}: ImageUrl={map.ImageUrl} RawImageUrl={map.RawImageUrl} monuments={map.TotalMonuments}");
var imageBytes = await http.GetByteArrayAsync(new Uri(map.ImageUrl!));
await File.WriteAllBytesAsync(Path.Combine(outDir, "rustmaps-render.png"), imageBytes);
await File.WriteAllTextAsync(Path.Combine(outDir, "monuments.json"),
    JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));

// Project every monument through OUR MapProjection onto THEIR render and drop crosshairs.
// If our math is right, every crosshair lands on the matching RustMaps monument icon.
using var overlay = Image.Load<Rgba32>(imageBytes);
var projection = new MapProjection((uint)size, overlay.Width, overlay.Height, OceanMarginPx: 0, OutputSize: overlay.Width);
overlay.Mutate(ctx =>
{
    foreach (var monument in map.Monuments ?? [])
    {
        if (monument.Coordinates is not { } c)
        {
            continue;
        }

        var (px, py) = projection.ToPixel(c.X, c.Y);
        ctx.DrawLine(Color.Magenta, 2f, new PointF(px - 12, py), new PointF(px + 12, py));
        ctx.DrawLine(Color.Magenta, 2f, new PointF(px, py - 12), new PointF(px, py + 12));
        Console.WriteLine($"{monument.Type,-30} world=({c.X,6},{c.Y,6}) grid={MapGrid.LabelFor(c.X, c.Y, (uint)size)} px=({px:F0},{py:F0})");
    }
});
await overlay.SaveAsPngAsync(Path.Combine(outDir, "overlay.png"));
Console.WriteLine($"Wrote {outDir}/overlay.png — crosshairs must sit on the RustMaps monument markers.");
return 0;
```

(Drop the unused `using RustMapsApi;` if the compiler flags it; the client needs only the configured `HttpClient`.)

README.md: two paragraphs — what it does, usage line, and "commit `monuments.json` to `tests/RustPlusBot.Features.Map.Tests/Fixtures/rustmaps-<size>-<seed>.json` to feed the parity test".

- [ ] **Step 2: Write the skippable parity test**

```csharp
// tests/RustPlusBot.Features.Map.Tests/RustMapsParityTests.cs
using System.Text.Json;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map.Tests;

/// <summary>
/// Ground-truth parity: monuments from a committed RustMaps response, projected through our
/// MapProjection, must land in the grid cells RustMaps world coordinates imply. Skips when no
/// fixture has been generated yet (tools/RustPlusBot.MapParity produces one).
/// </summary>
public sealed class RustMapsParityTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [SkippableFact]
    public void Monument_world_coords_project_into_consistent_grid_cells()
    {
        var fixture = Directory.Exists(FixtureDir)
            ? Directory.EnumerateFiles(FixtureDir, "rustmaps-*.json").FirstOrDefault()
            : null;
        Skip.If(fixture is null, "No RustMaps fixture committed yet (run tools/RustPlusBot.MapParity).");

        using var doc = JsonDocument.Parse(File.ReadAllText(fixture));
        var root = doc.RootElement;
        var size = (uint)root.GetProperty("size").GetInt32();
        var projection = new MapProjection(size, 2048, 2048, 0, 2048);

        foreach (var monument in root.GetProperty("monuments").EnumerateArray())
        {
            if (!monument.TryGetProperty("coordinates", out var coords) || coords.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var x = coords.GetProperty("x").GetSingle();
            var y = coords.GetProperty("y").GetSingle();
            var (px, py) = projection.ToPixel(x, y);

            // Independent re-derivation: the cell computed from the projected PIXEL must equal the
            // cell computed from the WORLD coordinate. Catches any scale/offset/Y-flip regression.
            var cells = MapGrid.CellCount(size);
            var cellPx = MapGrid.CellSize * (2048f / size);
            var colFromPixel = Math.Clamp((int)(px / cellPx), 0, cells - 1);
            var rowFromPixel = Math.Clamp((int)(py / cellPx), 0, cells - 1);
            var colFromWorld = Math.Clamp((int)(Math.Clamp(x, 0f, size - 1) / MapGrid.CellSize), 0, cells - 1);
            var rowFromWorld = cells - 1 - Math.Clamp((int)(Math.Clamp(y, 0f, size - 1) / MapGrid.CellSize), 0, cells - 1);
            Assert.Equal(colFromWorld, colFromPixel);
            Assert.Equal(rowFromWorld, rowFromPixel);
        }
    }
}
```

Csproj additions:

```xml
<ItemGroup>
  <PackageReference Include="Xunit.SkippableFact" />
</ItemGroup>
<ItemGroup>
  <Content Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

(JSON property casing: RustMaps v4 serializes camelCase — `size`, `monuments`, `coordinates.x` — matching `RustMapsJsonOptions` in the RustMapsApi repo; verify against the generated fixture when it lands.)

- [ ] **Step 3: Run + verify skip, build, commit**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter RustMapsParityTests`
Expected: 1 skipped (no fixture yet — the user generates and commits one after running the tool against their server's size/seed with their API key).

Run: `dotnet build RustPlusBot.slnx -warnaserror`
Expected: clean.

```bash
git add tools tests RustPlusBot.slnx
git commit -m "feat(map): RustMaps parity harness + skippable ground-truth test"
```

- [ ] **Step 4 (manual, user-driven verification gate — do not skip silently):** ask the user to run:

```bash
dotnet run --project tools/RustPlusBot.MapParity -- <their-size> <their-seed> <their-api-key> tmp/parity
```

and confirm the crosshairs in `tmp/parity/overlay.png` sit on RustMaps' monument icons. Copy `monuments.json` into `tests/RustPlusBot.Features.Map.Tests/Fixtures/rustmaps-<size>-<seed>.json`, re-run the parity test (now unskipped), and commit the fixture. Then a **live bot run**: connect to their server, compare the posted #map image against `tmp/images/official-app.jpg` (grid alignment, icon sizes, marker positions, trails after cargo moves).

---

### Task 12: Final verification sweep

**Files:** none new — whole-branch gates.

- [ ] **Step 1: Full sequential test run**

```bash
for p in tests/*/; do dotnet test "$p" --nologo | tail -2; done
```

Expected: every assembly green; record the total count (was 841 before this branch).

- [ ] **Step 2: Strict build + EF drift check**

```bash
dotnet build RustPlusBot.slnx -warnaserror
dotnet ef migrations has-pending-model-changes --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Persistence
```

Expected: clean build; no pending model changes (this branch adds no entities).

- [ ] **Step 3: Formatting gate**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git diff --stat
```

Expected: after cleanup, commit any reformat diff (or empty).

- [ ] **Step 4: Commit + wrap up**

```bash
git add -A src tests tools
git commit -m "chore(map): formatting + verification sweep for map render rework"
```

Then use superpowers:finishing-a-development-branch (PR to `develop`).

---

## Deferred (recorded, not planned)

- Wiring real `Rotation` values in `RustPlusSocketSource.AddMarkers` once RustPlusApi `2.0.0-beta.4` ships (one-line change per marker bucket + delete the note).
- Vending-machine markers layer, Steam avatars, zoom/crop views, GIF output, dead-reckoning.
