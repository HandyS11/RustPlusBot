# Subsystem 2b — Rendered live map in `#map` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render the live server map (base tile + grid + cargo/heli/chinook markers) as a PNG into a new per-server `#map` Discord channel, refreshed event-driven and throttled.

**Architecture:** New `RustPlusBot.Features.Map` project mirroring `Features.Events`. A pure ImageSharp pipeline (`WorldToPixel` + `MapRenderer`) draws overlays onto the static base map. A `BaseMapCache` singleton caches the base image per connection (fetched once via a new `IRustServerQuery.GetMapImageAsync` seam). A `MapHostedService` subscribes to `MapMarkersChangedEvent`/`ConnectionStatusChangedEvent`, throttles re-renders per `(guild,server)`, and posts via delete+repost (`DiscordMapChannelPoster`). Workspace gains a per-server `#map` channel + locator.

**Tech Stack:** .NET 10, C#, ImageSharp (`SixLabors.ImageSharp` + `SixLabors.ImageSharp.Drawing`), Discord.Net 3.20, xUnit + NSubstitute, EF Core (no migration this slice), RustPlusApi 2.0.0-beta.1.

## Global Constraints

- **Branch:** `feat/map-render` off `develop`. Plain branch in the main checkout — NO git worktree.
- **No entity, no migration, no new gateway intent** in this slice.
- **Strict analyzers:** Roslynator + SonarAnalyzer + NetAnalyzers, warnings-as-errors. `// TODO` comments are errors (S1135) — use XML `<remarks>` instead. Interface impls must repeat `= default` on optional params (S1006). Prefer concrete `Dictionary<>` over `IDictionary<>` where a private field (CA1859).
- **Format gate:** run `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` before pushing (the repo's real format gate; the pre-push hook enforces it). jb reorders members Roslynator does not flag.
- **No secrets in exception messages** (CA-style + repo rule).
- **Untested integration shims** (by repo convention): `DiscordMapChannelPoster` and `GetMapImageAsync` on `RustPlusSocketSource`. Everything else is unit-tested.
- **Run the FULL test suite and read per-assembly counts** after each task. A low total means an assembly failed to build (usually a test double missing a new interface member).
- **Output:** PNG, max edge ~1024 px.
- **Refresh throttle default:** `MapOptions.MapRefreshInterval = TimeSpan.FromSeconds(45)`.
- **Localized channel name:** EN `map`, FR `carte`.
- **Icon assets** treated as Facepunch/Rust game art (companion-app fair use), NOT GPL; recorded in a `NOTICE`. This slice may ship with drawn glyphs if icons are deferred — see Task 5.

---

## File Structure

**New project `src/RustPlusBot.Features.Map/`:**

- `RustPlusBot.Features.Map.csproj` — refs Abstractions, Persistence, Discord, Domain, Workspace, Connections; ImageSharp packages.
- `Rendering/WorldToPixel.cs` — pure coordinate mapper.
- `Rendering/MapLayerSet.cs` — value object (which layers to draw) + `Default2b` constant.
- `Rendering/MarkerPlacement.cs` — overlay item DTO (kind + pixel position).
- `Rendering/MapRenderer.cs` — base bytes + dims + placements + layers → PNG bytes.
- `Assets/MarkerGlyphs.cs` — per-`MarkerKind` glyph colour + letter (no external files this slice).
- `Composing/BaseMapCache.cs` — singleton per-`(guild,server)` base image+dims cache.
- `Composing/MapComposer.cs` — gathers cache + `IEventState` markers + layers → render input → PNG.
- `Posting/IMapChannelPoster.cs` + `Posting/DiscordMapChannelPoster.cs` — delete+repost (shim).
- `Hosting/MapHostedService.cs` — event loops + throttle gate.
- `MapOptions.cs` — refresh interval.
- `MapServiceCollectionExtensions.cs` — `AddMap()`.

**Modified:**

- `Directory.Packages.props` — ImageSharp package versions.
- `RustPlusBot.slnx` — add the project.
- `src/RustPlusBot.Abstractions/Connections/IRustServerQuery.cs` — add `GetMapImageAsync`.
- `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs` — add `GetMapImageAsync`.
- `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` — impl on both nested classes (shim).
- `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` — forward `GetMapImageAsync` to live socket.
- `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs` — `ServerMap = "map"`.
- `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs` — add `#map` spec row.
- `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs` — EN/FR `channel.map.name`.
- `src/RustPlusBot.Features.Workspace/Locating/` — add `IMapChannelLocator` + `MapChannelLocator` (copy of event locator) + register in `WorkspaceServiceCollectionExtensions`.
- `src/RustPlusBot.Host/Program.cs` — `AddOptions<MapOptions>()...ValidateOnStart()` + `AddMap()`.
- `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` + `FakeConnection` — implement `GetMapImageAsync`.

**New test project `tests/RustPlusBot.Features.Map.Tests/`** with the per-unit tests.

---

## Task 1: Scaffold the `Features.Map` project + ImageSharp packages

**Files:**

- Create: `src/RustPlusBot.Features.Map/RustPlusBot.Features.Map.csproj`
- Create: `tests/RustPlusBot.Features.Map.Tests/RustPlusBot.Features.Map.Tests.csproj`
- Modify: `Directory.Packages.props`
- Modify: `RustPlusBot.slnx`

**Interfaces:**

- Produces: the `RustPlusBot.Features.Map` assembly + its test assembly, both building empty/green.

- [ ] **Step 1: Add ImageSharp package versions to `Directory.Packages.props`**

Add these two lines alongside the existing `<PackageVersion>` entries (use the latest stable 3.x at restore time; pin whatever `dotnet add` resolves):

```xml
<PackageVersion Include="SixLabors.ImageSharp" Version="3.1.5" />
<PackageVersion Include="SixLabors.ImageSharp.Drawing" Version="2.1.4" />
```

- [ ] **Step 2: Create the feature csproj**

Copy `src/RustPlusBot.Features.Events/RustPlusBot.Features.Events.csproj` as the template (same TFM, analyzers, `InternalsVisibleTo` block). Set its references to: Abstractions, Persistence, Discord, Domain, Workspace, Connections project references, plus:

```xml
<ItemGroup>
  <PackageReference Include="SixLabors.ImageSharp" />
  <PackageReference Include="SixLabors.ImageSharp.Drawing" />
</ItemGroup>
```

Include `<InternalsVisibleTo Include="RustPlusBot.Features.Map.Tests" />` and `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` (NSubstitute on internals), matching the Events csproj.

- [ ] **Step 3: Create the test csproj**

Copy `tests/RustPlusBot.Features.Events.Tests/RustPlusBot.Features.Events.Tests.csproj`; change the `ProjectReference` to point at `RustPlusBot.Features.Map`.

- [ ] **Step 4: Add both projects to the solution**

Run: `dotnet sln RustPlusBot.slnx add src/RustPlusBot.Features.Map/RustPlusBot.Features.Map.csproj tests/RustPlusBot.Features.Map.Tests/RustPlusBot.Features.Map.Tests.csproj`

- [ ] **Step 5: Restore + build**

Run: `dotnet build RustPlusBot.slnx`
Expected: build succeeds 0 warnings / 0 errors (empty projects compile).

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props RustPlusBot.slnx src/RustPlusBot.Features.Map tests/RustPlusBot.Features.Map.Tests
git commit -m "chore(map): scaffold Features.Map project + ImageSharp packages"
```

---

## Task 2: `WorldToPixel` coordinate mapper

**Files:**

- Create: `src/RustPlusBot.Features.Map/Rendering/WorldToPixel.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/WorldToPixelTests.cs`

**Interfaces:**

- Consumes: `RustPlusBot.Features.Connections.Listening.MapDimensions` (`uint Width`, `uint Height`, `int OceanMargin`).
- Produces: `public static (float X, float Y) WorldToPixel.ToPixel(float worldX, float worldY, MapDimensions dims, int outputSize)`.

**Coordinate facts:** The playable world spans `[0, dims.Width]` in X and Y. The full map tile (including ocean margin on all sides) spans `[-OceanMargin, Width+OceanMargin]`. World Y is south→north (bottom-up); image Y is top-down → flip. `outputSize` is the square output edge in pixels (the rendered image is square — Rust maps are square).

- [ ] **Step 1: Write the failing tests**

```csharp
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Map.Rendering;
using Xunit;

namespace RustPlusBot.Features.Map.Tests;

public sealed class WorldToPixelTests
{
    private static readonly MapDimensions Dims = new(Width: 4000, Height: 4000, OceanMargin: 500);

    [Fact]
    public void Origin_world_maps_to_bottom_left_inside_margin()
    {
        // World (0,0) is the SW corner of the playable area. Full tile spans [-500, 4500] = 5000 units.
        // Pixel-per-unit at outputSize 1000 = 1000/5000 = 0.2. World x=0 -> (0 - (-500)) * 0.2 = 100.
        // World y=0 is the bottom -> image y = outputSize - 100 = 900.
        var (px, py) = WorldToPixel.ToPixel(0f, 0f, Dims, outputSize: 1000);

        Assert.Equal(100f, px, precision: 3);
        Assert.Equal(900f, py, precision: 3);
    }

    [Fact]
    public void Center_world_maps_to_center_pixel()
    {
        var (px, py) = WorldToPixel.ToPixel(2000f, 2000f, Dims, outputSize: 1000);

        Assert.Equal(500f, px, precision: 3);
        Assert.Equal(500f, py, precision: 3);
    }

    [Fact]
    public void North_edge_maps_higher_than_south_edge()
    {
        var (_, southY) = WorldToPixel.ToPixel(2000f, 0f, Dims, outputSize: 1000);
        var (_, northY) = WorldToPixel.ToPixel(2000f, 4000f, Dims, outputSize: 1000);

        Assert.True(northY < southY); // North is visually higher = smaller image-Y.
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter WorldToPixelTests`
Expected: FAIL — `WorldToPixel` does not exist.

- [ ] **Step 3: Implement `WorldToPixel`**

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>Maps world coordinates to pixel coordinates on a square rendered map tile.</summary>
public static class WorldToPixel
{
    /// <summary>Converts a world (x, y) to a pixel (x, y) on a square output of the given edge length.</summary>
    /// <param name="worldX">World X (west→east), in [0, Width].</param>
    /// <param name="worldY">World Y (south→north), in [0, Height].</param>
    /// <param name="dims">The map dimensions (width/height in game units + ocean margin).</param>
    /// <param name="outputSize">The output image edge length in pixels.</param>
    /// <returns>The pixel coordinate (origin top-left, Y down).</returns>
    public static (float X, float Y) ToPixel(float worldX, float worldY, MapDimensions dims, int outputSize)
    {
        ArgumentNullException.ThrowIfNull(dims);

        // The full tile (with ocean margin on each side) spans [-margin, Width+margin].
        var margin = dims.OceanMargin;
        var span = dims.Width + (2f * margin);
        var perUnit = outputSize / span;

        var px = (worldX + margin) * perUnit;
        // Flip Y: world south (0) is the visual bottom (image y = outputSize), world north is the top.
        var py = outputSize - ((worldY + margin) * perUnit);
        return (px, py);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter WorldToPixelTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Map/Rendering/WorldToPixel.cs tests/RustPlusBot.Features.Map.Tests/WorldToPixelTests.cs
git commit -m "feat(map): world-to-pixel coordinate mapper"
```

---

## Task 3: `MapLayerSet` + `MarkerPlacement` value objects

**Files:**

- Create: `src/RustPlusBot.Features.Map/Rendering/MapLayerSet.cs`
- Create: `src/RustPlusBot.Features.Map/Rendering/MarkerPlacement.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/MapLayerSetTests.cs`

**Interfaces:**

- Consumes: `RustPlusBot.Features.Connections.Listening.MarkerKind`.
- Produces:
  - `public sealed record MapLayerSet(bool Grid, bool Markers, bool Monuments, bool Vendor, bool Rigs)` with `public static MapLayerSet Default2b { get; }` = `new(Grid: true, Markers: true, Monuments: false, Vendor: false, Rigs: false)`.
  - `public sealed record MarkerPlacement(MarkerKind Kind, float PixelX, float PixelY)`.

- [ ] **Step 1: Write the failing test**

```csharp
using RustPlusBot.Features.Map.Rendering;
using Xunit;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapLayerSetTests
{
    [Fact]
    public void Default2b_enables_grid_and_markers_only()
    {
        var set = MapLayerSet.Default2b;

        Assert.True(set.Grid);
        Assert.True(set.Markers);
        Assert.False(set.Monuments);
        Assert.False(set.Vendor);
        Assert.False(set.Rigs);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapLayerSetTests`
Expected: FAIL — `MapLayerSet` does not exist.

- [ ] **Step 3: Implement both records**

`MapLayerSet.cs`:

```csharp
namespace RustPlusBot.Features.Map.Rendering;

/// <summary>Which overlay layers the renderer should draw. 2b uses <see cref="Default2b"/>; 2b-ii feeds this from per-server settings.</summary>
/// <param name="Grid">Draw the A0–Z grid lines and labels.</param>
/// <param name="Markers">Draw live cargo/heli/chinook markers.</param>
/// <param name="Monuments">Draw monument icons (2b-ii).</param>
/// <param name="Vendor">Draw the travelling-vendor marker (2b-ii).</param>
/// <param name="Rigs">Style oil rigs by activation state (2b-ii).</param>
public sealed record MapLayerSet(bool Grid, bool Markers, bool Monuments, bool Vendor, bool Rigs)
{
    /// <summary>The fixed layer set for subsystem 2b: grid + live markers on, the rest off.</summary>
    public static MapLayerSet Default2b { get; } =
        new(Grid: true, Markers: true, Monuments: false, Vendor: false, Rigs: false);
}
```

`MarkerPlacement.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>One marker to draw, already projected to pixel coordinates.</summary>
/// <param name="Kind">The marker kind (selects the glyph).</param>
/// <param name="PixelX">Pixel X on the rendered tile.</param>
/// <param name="PixelY">Pixel Y on the rendered tile.</param>
public sealed record MarkerPlacement(MarkerKind Kind, float PixelX, float PixelY);
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapLayerSetTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Map/Rendering/MapLayerSet.cs src/RustPlusBot.Features.Map/Rendering/MarkerPlacement.cs tests/RustPlusBot.Features.Map.Tests/MapLayerSetTests.cs
git commit -m "feat(map): MapLayerSet + MarkerPlacement value objects"
```

---

## Task 4: `MarkerGlyphs` (glyph colour + letter per marker kind)

**Files:**

- Create: `src/RustPlusBot.Features.Map/Assets/MarkerGlyphs.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/MarkerGlyphsTests.cs`

**Interfaces:**

- Consumes: `RustPlusBot.Features.Connections.Listening.MarkerKind`, `SixLabors.ImageSharp.Color`.
- Produces: `public static (SixLabors.ImageSharp.Color Color, string Letter) MarkerGlyphs.For(MarkerKind kind)`. Maps CargoShip→(Blue,"C"), PatrolHelicopter→(Red,"H"), Chinook→(Orange,"K"); everything else→(Gray,"?").

**Rationale:** This slice draws glyphs rather than vendoring icon files, keeping the renderer self-contained. `MarkerGlyphs` is the keyed registry the spec calls for — 2b-ii swaps it for image assets without changing `MapRenderer`.

- [ ] **Step 1: Write the failing test**

```csharp
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Map.Assets;
using SixLabors.ImageSharp;
using Xunit;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MarkerGlyphsTests
{
    [Theory]
    [InlineData(MarkerKind.CargoShip, "C")]
    [InlineData(MarkerKind.PatrolHelicopter, "H")]
    [InlineData(MarkerKind.Chinook, "K")]
    public void Known_kinds_have_distinct_letters(MarkerKind kind, string expected)
    {
        var (_, letter) = MarkerGlyphs.For(kind);
        Assert.Equal(expected, letter);
    }

    [Fact]
    public void Unknown_kind_falls_back_to_question_mark()
    {
        var (color, letter) = MarkerGlyphs.For(MarkerKind.Other);
        Assert.Equal("?", letter);
        Assert.NotEqual(default, color);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MarkerGlyphsTests`
Expected: FAIL — `MarkerGlyphs` does not exist.

- [ ] **Step 3: Implement**

```csharp
using RustPlusBot.Features.Connections.Listening;
using SixLabors.ImageSharp;

namespace RustPlusBot.Features.Map.Assets;

/// <summary>Maps a marker kind to a draw glyph (colour + single letter). The keyed registry the map renderer draws from; 2b-ii can swap this for image assets without changing the renderer.</summary>
public static class MarkerGlyphs
{
    /// <summary>Gets the glyph colour and letter for a marker kind.</summary>
    /// <param name="kind">The marker kind.</param>
    /// <returns>The colour and a one-character label.</returns>
    public static (Color Color, string Letter) For(MarkerKind kind) => kind switch
    {
        MarkerKind.CargoShip => (Color.DodgerBlue, "C"),
        MarkerKind.PatrolHelicopter => (Color.Red, "H"),
        MarkerKind.Chinook => (Color.Orange, "K"),
        _ => (Color.Gray, "?"),
    };
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MarkerGlyphsTests`
Expected: PASS (4 cases).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Map/Assets/MarkerGlyphs.cs tests/RustPlusBot.Features.Map.Tests/MarkerGlyphsTests.cs
git commit -m "feat(map): marker glyph registry (colour + letter per kind)"
```

---

## Task 5: `MapRenderer` (ImageSharp pipeline → PNG bytes)

**Files:**

- Create: `src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/MapRendererTests.cs`

**Interfaces:**

- Consumes: `WorldToPixel`, `MapLayerSet`, `MarkerPlacement`, `MarkerGlyphs`, `MapDimensions`, ImageSharp.
- Produces: `public byte[] MapRenderer.Render(byte[] baseJpeg, MapDimensions dims, IReadOnlyList<MarkerPlacement> markers, MapLayerSet layers)` returning PNG bytes. `MapRenderer` is a stateless class (registered singleton). Constant `public const int OutputSize = 1024;`.

**Behaviour:** Decode `baseJpeg` → resize to `OutputSize`×`OutputSize` → if `layers.Grid` draw grid lines + column/row labels (grid cell = `WorldToPixel` of every `GridDiameter`=146.25 world units, matching `GridReference`) → if `layers.Markers` draw each placement as a filled circle in its glyph colour with the letter centred → encode PNG. Markers already arrive as pixel coordinates (the composer projects them), so the renderer does not call `WorldToPixel` for markers — it uses it only for grid lines. Keep grid math simple: vertical/horizontal lines every `146.25` world units across `[0, Width]`.

- [ ] **Step 1: Write the failing tests**

```csharp
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Map.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapRendererTests
{
    private static readonly MapDimensions Dims = new(Width: 4000, Height: 4000, OceanMargin: 500);

    // A 64x64 solid-green JPEG, generated once in-test so the renderer has a real base image to decode.
    private static byte[] BaseJpeg()
    {
        using var img = new Image<Rgba32>(64, 64, new Rgba32(0, 128, 0));
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Render_produces_a_png_of_the_output_size()
    {
        var renderer = new MapRenderer();

        var bytes = renderer.Render(BaseJpeg(), Dims, markers: [], MapLayerSet.Default2b);

        using var result = Image.Load<Rgba32>(bytes);
        Assert.Equal(MapRenderer.OutputSize, result.Width);
        Assert.Equal(MapRenderer.OutputSize, result.Height);
    }

    [Fact]
    public void Render_with_a_marker_differs_from_render_without()
    {
        var renderer = new MapRenderer();
        var jpeg = BaseJpeg();

        var without = renderer.Render(jpeg, Dims, markers: [], new MapLayerSet(false, true, false, false, false));
        var with = renderer.Render(jpeg, Dims,
            markers: [new MarkerPlacement(MarkerKind.CargoShip, 512f, 512f)],
            new MapLayerSet(false, true, false, false, false));

        Assert.NotEqual(without, with); // The drawn marker changes the bytes.
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapRendererTests`
Expected: FAIL — `MapRenderer` does not exist.

- [ ] **Step 3: Implement `MapRenderer`**

```csharp
using System.Globalization;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Map.Assets;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>Renders the base map tile plus overlay layers to PNG bytes. Stateless; safe as a singleton.</summary>
public sealed class MapRenderer
{
    /// <summary>The square output edge length in pixels.</summary>
    public const int OutputSize = 1024;

    private const float GridDiameter = 146.25f;
    private const float MarkerRadius = 9f;

    private static readonly Font Font = SystemFonts.Collection.Families.Any()
        ? SystemFonts.CreateFont(SystemFonts.Families.First().Name, 12f)
        : throw new InvalidOperationException("No system font available for map rendering.");

    /// <summary>Renders the map.</summary>
    /// <param name="baseJpeg">The raw base-map JPEG bytes.</param>
    /// <param name="dims">The map dimensions.</param>
    /// <param name="markers">Marker placements already projected to pixel coordinates.</param>
    /// <param name="layers">Which layers to draw.</param>
    /// <returns>PNG bytes.</returns>
    public byte[] Render(byte[] baseJpeg, MapDimensions dims, IReadOnlyList<MarkerPlacement> markers, MapLayerSet layers)
    {
        ArgumentNullException.ThrowIfNull(baseJpeg);
        ArgumentNullException.ThrowIfNull(dims);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(layers);

        using var image = Image.Load<Rgba32>(baseJpeg);
        image.Mutate(ctx => ctx.Resize(OutputSize, OutputSize));

        if (layers.Grid)
        {
            DrawGrid(image, dims);
        }

        if (layers.Markers)
        {
            foreach (var m in markers)
            {
                DrawMarker(image, m);
            }
        }

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static void DrawGrid(Image<Rgba32> image, MapDimensions dims)
    {
        var pen = Pens.Solid(Color.FromRgba(255, 255, 255, 80), 1f);
        image.Mutate(ctx =>
        {
            for (var world = 0f; world <= dims.Width; world += GridDiameter)
            {
                var (vx, _) = WorldToPixel.ToPixel(world, 0f, dims, OutputSize);
                ctx.DrawLine(pen, new PointF(vx, 0), new PointF(vx, OutputSize));
                var (_, hy) = WorldToPixel.ToPixel(0f, world, dims, OutputSize);
                ctx.DrawLine(pen, new PointF(0, hy), new PointF(OutputSize, hy));
            }
        });
    }

    private static void DrawMarker(Image<Rgba32> image, MarkerPlacement m)
    {
        var (color, letter) = MarkerGlyphs.For(m.Kind);
        var circle = new SixLabors.ImageSharp.Drawing.EllipsePolygon(m.PixelX, m.PixelY, MarkerRadius);
        image.Mutate(ctx =>
        {
            ctx.Fill(color, circle);
            ctx.Draw(Pens.Solid(Color.Black, 1f), circle);
            var options = new RichTextOptions(Font)
            {
                Origin = new PointF(m.PixelX, m.PixelY),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ctx.DrawText(options, letter, Color.White);
        });
    }
}
```

> **Note for the implementer:** the exact ImageSharp.Drawing API (`EllipsePolygon`, `Pens.Solid`, `RichTextOptions`, `DrawText`) is from ImageSharp.Drawing 2.x. If a member name differs in the restored version, adjust to the equivalent — the test (PNG of correct size + marker changes bytes) is the contract, not the exact drawing calls. If no system font is available on the build/CI host, bundle a small open-licensed TTF as an embedded resource and load it via `new FontCollection().Add(stream)` instead of `SystemFonts`.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapRendererTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs tests/RustPlusBot.Features.Map.Tests/MapRendererTests.cs
git commit -m "feat(map): ImageSharp renderer (base tile + grid + marker glyphs -> PNG)"
```

---

## Task 6: Extend the connection seam with `GetMapImageAsync`

**Files:**

- Modify: `src/RustPlusBot.Abstractions/Connections/IRustServerQuery.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` (both `RejectedConnection` and `RustPlusServerConnection`)
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Modify: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` (and the nested `FakeConnection`)
- Test: `tests/RustPlusBot.Features.Connections.Tests/MapImageQueryTests.cs`

**Interfaces:**

- Produces:
  - On `IRustServerConnection` (internal): `Task<byte[]?> GetMapImageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)` — base JPEG bytes, null on failure/unavailable.
  - On `IRustServerQuery` (public, Abstractions): `Task<byte[]?> GetMapImageAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)` — null when no live socket.
  - On `FakeConnection`: a settable `byte[]? MapImageResult` returned by its `GetMapImageAsync`.

- [ ] **Step 1: Write the failing test (supervisor forwards to the live socket)**

Add to a new file. Mirror an existing supervisor test in `RustPlusBot.Features.Connections.Tests` for harness setup (shared-cache in-memory SQLite + the standard `EnsureConnectionAsync` flow). The assertion:

```csharp
// After EnsureConnectionAsync has produced a live socket for (guild, server),
// and source.LastConnection!.MapImageResult = new byte[] { 1, 2, 3 };
var image = await supervisor.GetMapImageAsync(guildId, serverId, CancellationToken.None);
Assert.Equal(new byte[] { 1, 2, 3 }, image);

// And when there is no live socket:
var none = await supervisor.GetMapImageAsync(guildId, Guid.NewGuid(), CancellationToken.None);
Assert.Null(none);
```

> Reuse the exact harness pattern from the nearest existing test that calls `EnsureConnectionAsync` and reads `source.LastConnection` (e.g. the marker/monument supervisor tests). Do not invent a new harness.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter MapImageQueryTests`
Expected: FAIL — `GetMapImageAsync` not defined.

- [ ] **Step 3: Add the interface members**

`IRustServerConnection.cs` — add inside the interface:

```csharp
    /// <summary>Gets the base map image (JPEG bytes), or null on failure/unavailable.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The base-map JPEG bytes, or null on failure/unavailable.</returns>
    Task<byte[]?> GetMapImageAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
```

`IRustServerQuery.cs` — add inside the interface:

```csharp
    /// <summary>Gets the base map image (JPEG bytes), or null when there is no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The base-map JPEG bytes, or null when there is no live socket.</returns>
    Task<byte[]?> GetMapImageAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
```

- [ ] **Step 4: Implement on the real shim (`RustPlusSocketSource.cs`)**

In `RejectedConnection` add:

```csharp
        public Task<byte[]?> GetMapImageAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);
```

In `RustPlusServerConnection` add (untested shim — same broad-catch pattern as `GetMapDimensionsAsync`):

```csharp
        public async Task<byte[]?> GetMapImageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.1): GetMapAsync -> Response<ServerMap>; ServerMap.JpgImage is the raw JPEG bytes.
                var response = await _rustPlus.GetMapAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                return response.IsSuccess ? response.Data?.JpgImage : null;
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

> Verify `ServerMap.JpgImage`'s exact type during execution (the XML doc says "Raw JPEG image bytes" → expected `byte[]?`). If it's a different type (e.g. `ReadOnlyMemory<byte>?`), convert with `.ToArray()`.

- [ ] **Step 5: Implement forwarding on `ConnectionSupervisor.cs`**

Add next to `GetServerInfoAsync` (copy that method's shape):

```csharp
    /// <inheritdoc />
    public async Task<byte[]?> GetMapImageAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return null;
        }

        return await live.Connection.GetMapImageAsync(_options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }
```

- [ ] **Step 6: Implement on the fake (`FakeRustSocketSource.cs` → `FakeConnection`)**

Add a settable property and the method to the nested `FakeConnection`:

```csharp
        public byte[]? MapImageResult { get; set; }

        public Task<byte[]?> GetMapImageAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(MapImageResult);
```

> If any OTHER test double implements `IRustServerConnection` or `IRustServerQuery` (search the test tree), add the member there too, or that assembly will fail to build.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test RustPlusBot.slnx`
Expected: all assemblies green; Connections gains the new test. Read per-assembly counts — none should drop.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Abstractions/Connections/IRustServerQuery.cs src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(connections): GetMapImageAsync seam (base map JPEG bytes)"
```

---

## Task 7: `BaseMapCache` singleton

**Files:**

- Create: `src/RustPlusBot.Features.Map/Composing/BaseMapCache.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/BaseMapCacheTests.cs`

**Interfaces:**

- Consumes: `IRustServerQuery.GetMapImageAsync`, `IRustServerQuery.GetTeamInfoAsync` is NOT used; uses `MapDimensions` from... — dims come from the marker event, so the cache stores only the image bytes keyed by `(guild,server)`. (Dims are supplied per-render by the composer from `MapMarkersChangedEvent.Dimensions` / `ActiveMarker.Dimensions`.)
- Produces:
  - `public sealed class BaseMapCache` (singleton).
  - `public async Task<byte[]?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)` — returns cached bytes; on miss, fetches via `IRustServerQuery.GetMapImageAsync`, caches non-null, returns it.
  - `public void Clear(ulong guildId, Guid serverId)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using NSubstitute;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Map.Composing;
using Xunit;

namespace RustPlusBot.Features.Map.Tests;

public sealed class BaseMapCacheTests
{
    private static readonly Guid Server = Guid.NewGuid();
    private const ulong Guild = 1UL;

    [Fact]
    public async Task GetAsync_fetches_once_then_serves_from_cache()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(new byte[] { 9 });
        var cache = new BaseMapCache(query);

        var first = await cache.GetAsync(Guild, Server, CancellationToken.None);
        var second = await cache.GetAsync(Guild, Server, CancellationToken.None);

        Assert.Equal(new byte[] { 9 }, first);
        Assert.Equal(new byte[] { 9 }, second);
        await query.Received(1).GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_does_not_cache_null_and_retries()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns((byte[]?)null, new byte[] { 7 });
        var cache = new BaseMapCache(query);

        var first = await cache.GetAsync(Guild, Server, CancellationToken.None);
        var second = await cache.GetAsync(Guild, Server, CancellationToken.None);

        Assert.Null(first);
        Assert.Equal(new byte[] { 7 }, second);
        await query.Received(2).GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clear_evicts_so_next_get_refetches()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(new byte[] { 1 });
        var cache = new BaseMapCache(query);

        await cache.GetAsync(Guild, Server, CancellationToken.None);
        cache.Clear(Guild, Server);
        await cache.GetAsync(Guild, Server, CancellationToken.None);

        await query.Received(2).GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter BaseMapCacheTests`
Expected: FAIL — `BaseMapCache` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Concurrent;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Map.Composing;

/// <summary>Caches the static-per-wipe base map image per (guild, server). Singleton so the cache survives across refreshes.</summary>
/// <param name="query">The live query seam used to fetch the base map on a cache miss.</param>
public sealed class BaseMapCache(IRustServerQuery query)
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), byte[]> _images = new();

    /// <summary>Gets the cached base map, fetching and caching it on a miss. Null results are not cached.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The base-map JPEG bytes, or null if unavailable.</returns>
    public async Task<byte[]?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        if (_images.TryGetValue((guildId, serverId), out var cached))
        {
            return cached;
        }

        var fetched = await query.GetMapImageAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (fetched is not null)
        {
            _images[(guildId, serverId)] = fetched;
        }

        return fetched;
    }

    /// <summary>Evicts the cached base map for a server (called on disconnect).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    public void Clear(ulong guildId, Guid serverId) => _images.TryRemove((guildId, serverId), out _);
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter BaseMapCacheTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Map/Composing/BaseMapCache.cs tests/RustPlusBot.Features.Map.Tests/BaseMapCacheTests.cs
git commit -m "feat(map): base-map cache singleton"
```

---

## Task 8: `MapComposer` (gather data → PNG, or null)

**Files:**

- Create: `src/RustPlusBot.Features.Map/Composing/MapComposer.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/MapComposerTests.cs`

**Interfaces:**

- Consumes: `BaseMapCache`, `RustPlusBot.Features.Events.State.IEventState` (`GetActiveMarkers(guild, server, MarkerKind)` → `IReadOnlyList<ActiveMarker>` with `X`, `Y`, `Dimensions`), `MapRenderer`, `WorldToPixel`, `MapLayerSet`.
- Produces:
  - `public sealed class MapComposer(BaseMapCache cache, IEventState events, MapRenderer renderer)`.
  - `public async Task<byte[]?> ComposeAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)` — returns PNG bytes, or null when no base map yet or no dims known.

**Behaviour:** Fetch base map from `cache`; if null → return null. Gather active markers (CargoShip, PatrolHelicopter, Chinook) from `IEventState`. Determine `MapDimensions` from the first marker that has non-null `Dimensions`; if none has dims and there are no markers → can't draw grid, but still render the base map with `MapDimensions` unavailable → in that case return the base map rendered with `MapLayerSet` Grid forced off. To keep it simple and testable: if no dims available, render base only (no grid, no markers). Project each marker via `WorldToPixel.ToPixel` using the resolved dims → `MarkerPlacement`. Call `renderer.Render(base, dims, placements, MapLayerSet.Default2b)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using NSubstitute;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapComposerTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly MapDimensions Dims = new(4000, 4000, 500);

    private static byte[] BaseJpeg()
    {
        using var img = new Image<Rgba32>(64, 64, new Rgba32(0, 128, 0));
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static MapComposer Build(byte[]? baseImage, params ActiveMarker[] markers)
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetMapImageAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(baseImage);
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(Guild, Server, Arg.Any<MarkerKind>())
            .Returns(ci => markers.Where(m => m.Kind == (MarkerKind)ci[2]!).ToList());
        return new MapComposer(new BaseMapCache(query), events, new MapRenderer());
    }

    [Fact]
    public async Task Returns_null_when_no_base_map_available()
    {
        var composer = Build(baseImage: null);

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.Null(png);
    }

    [Fact]
    public async Task Renders_a_png_when_base_map_available()
    {
        var marker = new ActiveMarker(1, MarkerKind.CargoShip, 2000f, 2000f, Dims, DateTimeOffset.UtcNow);
        var composer = Build(BaseJpeg(), marker);

        var png = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(png);
        using var result = Image.Load<Rgba32>(png!);
        Assert.Equal(MapRenderer.OutputSize, result.Width);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapComposerTests`
Expected: FAIL — `MapComposer` does not exist.

- [ ] **Step 3: Implement**

```csharp
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map.Composing;

/// <summary>Gathers the cached base map + live markers and renders the map PNG.</summary>
/// <param name="cache">The base-map cache.</param>
/// <param name="events">Live marker state.</param>
/// <param name="renderer">The image renderer.</param>
public sealed class MapComposer(BaseMapCache cache, IEventState events, MapRenderer renderer)
{
    private static readonly MarkerKind[] DrawnKinds =
    [
        MarkerKind.CargoShip, MarkerKind.PatrolHelicopter, MarkerKind.Chinook,
    ];

    /// <summary>Composes the map PNG for a server, or null when no base map is available yet.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>PNG bytes, or null.</returns>
    public async Task<byte[]?> ComposeAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var baseImage = await cache.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (baseImage is null)
        {
            return null;
        }

        var active = DrawnKinds
            .SelectMany(kind => events.GetActiveMarkers(guildId, serverId, kind))
            .ToList();

        var dims = active.Select(m => m.Dimensions).FirstOrDefault(d => d is not null);
        if (dims is null)
        {
            // No dimensions known yet: render the base tile only (no grid/markers need world→pixel).
            return renderer.Render(baseImage, new MapDimensions(0, 0, 0), markers: [],
                new MapLayerSet(Grid: false, Markers: false, Monuments: false, Vendor: false, Rigs: false));
        }

        var placements = active.Select(m =>
        {
            var (px, py) = WorldToPixel.ToPixel(m.X, m.Y, dims, MapRenderer.OutputSize);
            return new MarkerPlacement(m.Kind, px, py);
        }).ToList();

        return renderer.Render(baseImage, dims, placements, MapLayerSet.Default2b);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapComposerTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Map/Composing/MapComposer.cs tests/RustPlusBot.Features.Map.Tests/MapComposerTests.cs
git commit -m "feat(map): composer gathers base map + markers into a PNG"
```

---

## Task 9: `IMapChannelPoster` + `DiscordMapChannelPoster` (delete+repost shim)

**Files:**

- Create: `src/RustPlusBot.Features.Map/Posting/IMapChannelPoster.cs`
- Create: `src/RustPlusBot.Features.Map/Posting/DiscordMapChannelPoster.cs`

**Interfaces:**

- Produces: `internal interface IMapChannelPoster { Task PostAsync(ulong channelId, byte[] pngBytes, CancellationToken cancellationToken); }` and `DiscordMapChannelPoster` implementing it.

**Behaviour (untested shim, mirrors `DiscordEventChannelPoster`):** Resolve the channel via `client.GetChannelAsync`; if not an `ITextChannel`, return. Delete the bot's own prior messages in the channel (fetch recent, filter `Author.Id == client.CurrentUser.Id`, bulk/iterative delete) — best-effort. Then `SendFileAsync` the PNG as `map.png` with `AllowedMentions.None`. Broad-catch so a Discord hiccup never crashes the loop; rethrow `OperationCanceledException`.

- [ ] **Step 1: Write the interface**

```csharp
namespace RustPlusBot.Features.Map.Posting;

/// <summary>Posts the rendered map image to a Discord channel, replacing the prior bot message.</summary>
internal interface IMapChannelPoster
{
    /// <summary>Deletes the bot's prior map message (if any) and posts the new PNG.</summary>
    /// <param name="channelId">The #map channel id.</param>
    /// <param name="pngBytes">The rendered PNG.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the post is issued.</returns>
    Task PostAsync(ulong channelId, byte[] pngBytes, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Implement `DiscordMapChannelPoster`**

```csharp
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Map.Posting;

/// <summary>Posts the map image to Discord by deleting the bot's prior message and reposting. Untested integration shim.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordMapChannelPoster(
    DiscordSocketClient client,
    ILogger<DiscordMapChannelPoster> logger) : IMapChannelPoster
{
    private const int RecentMessageScan = 10;

    /// <inheritdoc />
    public async Task PostAsync(ulong channelId, byte[] pngBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        try
        {
            var options = new RequestOptions { CancelToken = cancellationToken };
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false) is not ITextChannel channel)
            {
                return;
            }

            await DeletePriorBotMessagesAsync(channel, options).ConfigureAwait(false);

            using var stream = new MemoryStream(pngBytes);
            await channel.SendFileAsync(stream, "map.png", allowedMentions: AllowedMentions.None, options: options)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown: let the loop unwind.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the map loop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPostFailed(logger, ex, channelId);
        }
    }

    private async Task DeletePriorBotMessagesAsync(ITextChannel channel, RequestOptions options)
    {
        var batch = await channel.GetMessagesAsync(RecentMessageScan, options: options).FlattenAsync()
            .ConfigureAwait(false);
        foreach (var message in batch.Where(m => m.Author.Id == client.CurrentUser.Id))
        {
            await message.DeleteAsync(options).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting the map image to channel {ChannelId} failed.")]
    private static partial void LogPostFailed(ILogger logger, Exception exception, ulong channelId);
}
```

- [ ] **Step 3: Build (no test for the shim, by convention)**

Run: `dotnet build src/RustPlusBot.Features.Map`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Features.Map/Posting
git commit -m "feat(map): Discord map-channel poster (delete+repost shim)"
```

---

## Task 10: Workspace `#map` channel + `IMapChannelLocator`

**Files:**

- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/IMapChannelLocator.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/MapChannelLocator.cs`
- Modify: `src/RustPlusBot.Features.Workspace/` DI extension (register `MapChannelLocator`)
- Test: `tests/RustPlusBot.Features.Workspace.Tests/MapChannelLocatorTests.cs` (copy the `EventChannelLocator` test) + update the spec-provider channel-count test if one exists.

**Interfaces:**

- Produces: `public const string WorkspaceChannelKeys.ServerMap = "map";`; `public interface IMapChannelLocator { Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken); }`; `MapChannelLocator` implementing it.

- [ ] **Step 1: Add the channel key**

In `WorkspaceKeys.cs`, alongside `ServerEvents`:

```csharp
    /// <summary>The per-server rendered-map channel.</summary>
    public const string ServerMap = "map";
```

- [ ] **Step 2: Add the spec row**

In `ServerWorkspaceSpecProvider.GetChannelSpecs()`, append:

```csharp
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerMap, "channel.map.name",
            ChannelPermissionProfile.ReadOnly, 3),
```

- [ ] **Step 3: Add EN/FR channel names**

In `LocalizationCatalog.cs`, add to the EN dictionary `["channel.map.name"] = "map",` and to the FR dictionary `["channel.map.name"] = "carte",`.

- [ ] **Step 4: Write the failing locator test**

Copy `tests/RustPlusBot.Features.Workspace.Tests/EventChannelLocatorTests.cs` to `MapChannelLocatorTests.cs`, replacing the type names and the channel key with `WorkspaceChannelKeys.ServerMap`. (If no such test file exists, write one mirroring the `EventChannelLocator` TTL/resolve assertions, seeding a provisioned `#map` channel via the same store fake the event-locator test uses.)

- [ ] **Step 5: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter MapChannelLocator`
Expected: FAIL — `IMapChannelLocator`/`MapChannelLocator` not defined.

- [ ] **Step 6: Implement the locator**

Copy `Locating/EventChannelLocator.cs` → `MapChannelLocator.cs` and `Locating/IEventChannelLocator.cs` → `IMapChannelLocator.cs` verbatim, changing: type names, and the channel key passed to `GetChannelsByKeyAsync` to `WorkspaceChannelKeys.ServerMap`. Keep the 30 s TTL, the `IClock` dependency, and `IDisposable`.

- [ ] **Step 7: Register it**

In the Workspace DI extension (where `EventChannelLocator`/`IEventChannelLocator` is registered — search for `IEventChannelLocator`), add the analogous singleton registration for `MapChannelLocator`/`IMapChannelLocator`.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test RustPlusBot.slnx`
Expected: green; Workspace gains the locator test. If a channel-count assertion in `ServerWorkspaceSpecProvider` tests exists, bump it from 3 to 4.

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests
git commit -m "feat(workspace): per-server #map channel + map channel locator"
```

---

## Task 11: `MapOptions` + `MapHostedService` (throttle gate + event loops)

**Files:**

- Create: `src/RustPlusBot.Features.Map/MapOptions.cs`
- Create: `src/RustPlusBot.Features.Map/Hosting/MapHostedService.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/MapRefreshThrottleTests.cs`

**Interfaces:**

- Consumes: `IEventBus.SubscribeAsync<MapMarkersChangedEvent>` / `<ConnectionStatusChangedEvent>` (ns `RustPlusBot.Abstractions.Events`), `MapComposer`, `BaseMapCache`, `IMapChannelLocator`, `IMapChannelPoster`, `IClock`, `IOptions<MapOptions>`, `IServiceScopeFactory` (for scoped `IConnectionStore` on disconnect, mirroring `EventsHostedService.ClearIfDisconnectedAsync`).
- Produces:
  - `public sealed class MapOptions { public TimeSpan MapRefreshInterval { get; set; } = TimeSpan.FromSeconds(45); }`.
  - An **internal testable throttle** `MapRefreshThrottle(IClock clock)` with `public bool ShouldRefresh(ulong guildId, Guid serverId, TimeSpan interval)` returning true at most once per interval per key. Extract this so the gate is unit-tested without a running host.

**Behaviour:** Two loops (like `EventsHostedService`). Marker-loop: on `MapMarkersChangedEvent`, if `throttle.ShouldRefresh(...)` → compose PNG → resolve channel → post. Disconnect-loop: on `ConnectionStatusChangedEvent`, open a scope, read `IConnectionStore.GetStateAsync`; if null or not `Connected` → `cache.Clear(...)` (the next connect re-fetches the base map). Broad-catch each loop. Same Start/Stop/Dispose CTS pattern as `EventsHostedService`.

- [ ] **Step 1: Write the failing throttle test**

```csharp
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Map.Hosting;
using Xunit;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapRefreshThrottleTests
{
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }

    private static readonly Guid Server = Guid.NewGuid();
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(45);

    [Fact]
    public void First_call_allows_then_blocks_within_interval()
    {
        var clock = new TestClock();
        var throttle = new MapRefreshThrottle(clock);

        Assert.True(throttle.ShouldRefresh(1UL, Server, Interval));
        clock.UtcNow = clock.UtcNow.AddSeconds(10);
        Assert.False(throttle.ShouldRefresh(1UL, Server, Interval));
    }

    [Fact]
    public void Allows_again_after_interval_elapses()
    {
        var clock = new TestClock();
        var throttle = new MapRefreshThrottle(clock);

        Assert.True(throttle.ShouldRefresh(1UL, Server, Interval));
        clock.UtcNow = clock.UtcNow.AddSeconds(46);
        Assert.True(throttle.ShouldRefresh(1UL, Server, Interval));
    }

    [Fact]
    public void Separate_servers_throttle_independently()
    {
        var clock = new TestClock();
        var throttle = new MapRefreshThrottle(clock);
        var other = Guid.NewGuid();

        Assert.True(throttle.ShouldRefresh(1UL, Server, Interval));
        Assert.True(throttle.ShouldRefresh(1UL, other, Interval));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapRefreshThrottleTests`
Expected: FAIL — `MapRefreshThrottle` not defined.

- [ ] **Step 3: Implement `MapOptions`, `MapRefreshThrottle`, and `MapHostedService`**

`MapOptions.cs`:

```csharp
namespace RustPlusBot.Features.Map;

/// <summary>Map feature configuration, bound from the "Map" config section.</summary>
public sealed class MapOptions
{
    /// <summary>Minimum time between #map image re-renders per server (coalesces rapid marker changes). Default 45s.</summary>
    public TimeSpan MapRefreshInterval { get; set; } = TimeSpan.FromSeconds(45);
}
```

`Hosting/MapRefreshThrottle.cs`:

```csharp
using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Map.Hosting;

/// <summary>Per-(guild,server) gate that allows a refresh at most once per interval.</summary>
/// <param name="clock">Supplies the current time.</param>
internal sealed class MapRefreshThrottle(IClock clock)
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), DateTimeOffset> _last = new();

    /// <summary>Returns true if a refresh is due for the key, recording the time when it returns true.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="interval">The minimum spacing between refreshes.</param>
    /// <returns>True at most once per interval per key.</returns>
    public bool ShouldRefresh(ulong guildId, Guid serverId, TimeSpan interval)
    {
        var now = clock.UtcNow;
        var key = (guildId, serverId);
        if (_last.TryGetValue(key, out var last) && now - last < interval)
        {
            return false;
        }

        _last[key] = now;
        return true;
    }
}
```

`Hosting/MapHostedService.cs` — model it on `EventsHostedService` (CTS + `Task.Run` loops + `StopAsync` join + broad-catch). Two loops:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Posting;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Map.Hosting;

/// <summary>Re-renders and reposts the #map image on marker changes (throttled) and clears caches on disconnect.</summary>
internal sealed partial class MapHostedService(
    IEventBus eventBus,
    MapComposer composer,
    BaseMapCache cache,
    IMapChannelLocator locator,
    IMapChannelPoster poster,
    IClock clock,
    IOptions<MapOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<MapHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly MapRefreshThrottle _throttle = new(clock);
    private Task? _markerLoop;
    private Task? _disconnectLoop;

    public void Dispose() => _cts.Dispose();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _markerLoop = Task.Run(() => ConsumeMarkerEventsAsync(_cts.Token), CancellationToken.None);
        _disconnectLoop = Task.Run(() => ConsumeConnectionStatusEventsAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[] { _markerLoop, _disconnectLoop }.Where(t => t is not null))
        {
            try
            {
#pragma warning disable VSTHRD003
                await loop!.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private async Task ConsumeMarkerEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<MapMarkersChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await RefreshAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogMarkerLoopFaulted(logger, ex);
        }
    }

    private async Task RefreshAsync(ulong guildId, System.Guid serverId, CancellationToken cancellationToken)
    {
        if (!_throttle.ShouldRefresh(guildId, serverId, options.Value.MapRefreshInterval))
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (channelId is not { } id)
        {
            return;
        }

        var png = await composer.ComposeAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (png is null)
        {
            return;
        }

        await poster.PostAsync(id, png, cancellationToken).ConfigureAwait(false);
    }

    private async Task ConsumeConnectionStatusEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await ClearIfDisconnectedAsync(evt, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDisconnectLoopFaulted(logger, ex);
        }
    }

    private async Task ClearIfDisconnectedAsync(ConnectionStatusChangedEvent evt, CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var connectionStore = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var state = await connectionStore.GetStateAsync(evt.GuildId, evt.ServerId, cancellationToken)
                .ConfigureAwait(false);
            if (state is null || state.Status != ConnectionStatus.Connected)
            {
                cache.Clear(evt.GuildId, evt.ServerId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Map marker loop faulted.")]
    private static partial void LogMarkerLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Map disconnect-clear loop faulted.")]
    private static partial void LogDisconnectLoopFaulted(ILogger logger, Exception exception);
}
```

> Confirm the exact namespaces of `IEventBus` and `ConnectionStatusChangedEvent` from `EventsHostedService`'s `using`s (they are `RustPlusBot.Features.Connections` / `RustPlusBot.Abstractions.Events` respectively — copy them verbatim).

- [ ] **Step 4: Run to verify the throttle test passes**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapRefreshThrottleTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Build**

Run: `dotnet build src/RustPlusBot.Features.Map`
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Map/MapOptions.cs src/RustPlusBot.Features.Map/Hosting tests/RustPlusBot.Features.Map.Tests/MapRefreshThrottleTests.cs
git commit -m "feat(map): hosted service with per-server refresh throttle + disconnect-clear"
```

---

## Task 12: `AddMap()` DI extension + Host wiring + registration test

**Files:**

- Create: `src/RustPlusBot.Features.Map/MapServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Host/Program.cs`
- Create: `tests/RustPlusBot.Features.Map.Tests/MapRegistrationTests.cs`

**Interfaces:**

- Produces: `public static IServiceCollection AddMap(this IServiceCollection services)`.

- [ ] **Step 1: Write `MapServiceCollectionExtensions`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Hosting;
using RustPlusBot.Features.Map.Posting;
using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map;

/// <summary>DI registration for the map-render feature.</summary>
public static class MapServiceCollectionExtensions
{
    /// <summary>Registers the renderer, base-map cache, composer, poster, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddMap(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<MapRenderer>();
        services.AddSingleton<BaseMapCache>();
        services.AddSingleton<MapComposer>();
        services.AddSingleton<IMapChannelPoster, DiscordMapChannelPoster>();
        services.AddHostedService<MapHostedService>();

        return services;
    }
}
```

> `IRustServerQuery`, `IEventState`, `IMapChannelLocator`, `IEventBus`, `IClock`, `IConnectionStore`, `DiscordSocketClient`, and `IOptions<MapOptions>` are all already registered by `AddConnections`/`AddEvents`/`AddWorkspace`/the Host. `AddMap` registers only this feature's own types.

- [ ] **Step 2: Wire into `Program.cs`**

After the `AddEvents()` line, add the options block (mirroring `ConnectionOptions`) and `AddMap()`:

```csharp
builder.Services.AddOptions<MapOptions>()
    .BindConfiguration("Map")
    .Validate(static o => o.MapRefreshInterval > TimeSpan.Zero, "Map:MapRefreshInterval must be positive.")
    .ValidateOnStart();
builder.Services.AddMap();
```

(Match the exact `.BindConfiguration("...")` form used by the neighbouring options blocks — copy whichever binding call `AddOptions<ConnectionOptions>()` uses.)

- [ ] **Step 3: Write the registration test**

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Map;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Rendering;
using Xunit;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapRegistrationTests
{
    [Fact]
    public void AddMap_registers_renderer_and_composer_singletons()
    {
        var services = new ServiceCollection();
        services.AddMap();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<MapRenderer>());
        Assert.NotNull(provider.GetRequiredService<BaseMapCache>()); // resolves only if IRustServerQuery is provided
    }
}
```

> `BaseMapCache` depends on `IRustServerQuery`; the test must register a substitute for it (and any other ctor dep) before `BuildServiceProvider`, OR assert only `MapRenderer` (no deps) and the presence of the descriptor for `BaseMapCache`. Prefer registering an NSubstitute `IRustServerQuery` so the resolve actually succeeds, mirroring how `EventsRegistration`/`ChatRegistration` tests register their externals.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test RustPlusBot.slnx`
Expected: all green; Map test assembly present with its full count.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Map/MapServiceCollectionExtensions.cs src/RustPlusBot.Host/Program.cs tests/RustPlusBot.Features.Map.Tests/MapRegistrationTests.cs
git commit -m "feat(map): AddMap DI extension + host wiring + MapOptions validation"
```

---

## Task 13: Final verification, format gate, NOTICE

**Files:**

- Create/Modify: `NOTICE` (or append to README) — icon/asset attribution.

- [ ] **Step 1: Record asset attribution**

Add a short `NOTICE` note: this slice draws marker glyphs (no third-party image assets bundled). If/when icon PNGs are added (2b-ii), they originate as Facepunch/Rust game art used under companion-app norms, not under the GPL of the repos that redistribute them. (This documents the decision even though 2b ships glyphs.)

- [ ] **Step 2: Full build + test**

Run: `dotnet build RustPlusBot.slnx` then `dotnet test RustPlusBot.slnx`
Expected: build 0 warnings / 0 errors; all assemblies green. Read per-assembly counts and record the new totals (Map.Tests should report ~14–16).

- [ ] **Step 3: EF no-drift check**

Confirm no migration/model files changed on the branch:
Run: `git diff --name-only develop... | grep -iE "Migrations|ModelSnapshot|DbContext" || echo "no EF drift"`
Expected: `no EF drift`.

- [ ] **Step 4: Format gate (the repo's real gate)**

Run: `dotnet tool restore && dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
Then: `git status` — review what jb reordered; all touched files should be 2b branch files.

- [ ] **Step 5: Commit any jb changes**

```bash
git add -A
git commit -m "style(map): apply jb ReformatAndReorder"
```

> Do NOT `git add` anything under `docs/superpowers/` — it is gitignored and must never be committed.

- [ ] **Step 6: Verify the branch is clean and ready**

Run: `git status` (clean) and `git log --oneline develop..HEAD` (review the task commits).

---

## Self-Review

**1. Spec coverage:**

- §1 `#map` channel + one message + delete/repost + throttle + no slash command → Tasks 9, 10, 11. ✓
- §2 2b scope (project, pipeline, channel, GetMapImageAsync+cache, base+grid+markers fixed-on, hosted service, poster, locator, options, no entity) → Tasks 1–12. ✓ 2b-ii items explicitly NOT built. ✓
- §3 source data (JpgImage; markers from IEventState; MapMarkersChangedEvent trigger) → Tasks 6, 8, 11. ✓
- §4 unit table (WorldToPixel, MapLayerSet, MarkerPlacement, MapRenderer, MapAssets→MarkerGlyphs, MapComposer, poster, hosted service, localizer, DI) → Tasks 2–12. **Note:** the spec's "MapLocalizationCatalog/localizer" unit is dropped — there is no in-image/embed text in this slice (the channel name is localized by Workspace's catalog in Task 10), so a separate Map localizer would be dead code (YAGNI). Recorded as a conscious deviation.
- §4 base-map cache as a singleton + IRustServerQuery.GetMapImageAsync → Tasks 6, 7. ✓
- §5 Workspace #map spec + name + locator (not a MessageSpec) → Task 10. ✓
- §6 ~1024px PNG + ImageSharp packages → Tasks 1, 5. ✓
- §7 icon licensing / glyph fallback → Task 4 (glyphs) + Task 13 (NOTICE). ✓
- §8 tested units + untested shims + fake updates + full-suite discipline → every task's test step + Tasks 6, 9. ✓
- §9 forward-coupling guards (explicit MapLayerSet, keyed registry, no "3 buckets") → Tasks 3, 4, 8. ✓
- §10 non-goals → none built. ✓

**2. Placeholder scan:** No TBD/TODO. Each code step shows complete code. The two "verify the exact API member at execution" notes (ServerMap.JpgImage type in Task 6; ImageSharp.Drawing member names in Task 5) are real integration-shim caveats with a concrete fallback, not placeholders.

**3. Type consistency:** `GetMapImageAsync` signatures match across `IRustServerConnection` (timeout overload) / `IRustServerQuery` (guild,server overload) / `ConnectionSupervisor` / `FakeConnection` / `BaseMapCache` consumer. `MapRenderer.Render(byte[], MapDimensions, IReadOnlyList<MarkerPlacement>, MapLayerSet)` used identically in Tasks 5 and 8. `MarkerPlacement(MarkerKind, float, float)` consistent. `MapLayerSet.Default2b` consistent. `ShouldRefresh(ulong, Guid, TimeSpan)` consistent in Task 11. `IMapChannelLocator.GetChannelIdAsync` matches the `EventChannelLocator` shape. `BaseMapCache.GetAsync/Clear` consistent across Tasks 7, 8, 11.

All consistent.
