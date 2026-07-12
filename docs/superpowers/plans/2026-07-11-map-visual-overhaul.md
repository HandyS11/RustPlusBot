# Map Visual Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve the `#map` render — real correctly-placed event-icon rotors (heli 1 / CH47 2), subtle trails, a separate toggleable train-tunnel layer, and per-player colored crosses with a color→player→status legend embed.

**Architecture:** ImageSharp composites overlay layers onto a 1024px base tile in `MapRenderer`; `MapComposer` gathers live state into placements; per-server layer toggles persist via `MapSettingsStore`/`MapLayerSettings` and are driven by buttons in `MapControlMessageRenderer`. This plan (1) builds event icons from body+rotor parts, (2) softens trails, (3) adds a `Tunnels` layer end-to-end (persistence + UI + render routing), and (4) replaces the player circle with palette-colored crosses plus a legend attached to the image message as a Discord embed.

**Tech Stack:** C# / .NET 10, SixLabors.ImageSharp (+ Drawing), SkiaSharp + Svg.Skia (monument SVG raster), EF Core 10 (SQLite), Discord.Net, xUnit + NSubstitute.

## Global Constraints

- Solution file is `RustPlusBot.slnx` (no `.sln`). Build with `dotnet build RustPlusBot.slnx`.
- **`-maxcpucount:1` is MANDATORY on EVERY `dotnet build` AND `dotnet test`.** A `ConfigureGitHooks BeforeTargets="Build"` target races on `.git/config` under parallel builds; a failed build silently DROPS an assembly's tests (they report 0 and look "passing"). Always read the per-assembly test counts, never just "passed".
- Run `dotnet tool restore` once before the first build (jb / ef / stryker / docfx are local manifest tools).
- Build is `-warnaserror` (`TreatWarningsAsErrors=true` in `Directory.Build.props`): every public type/member needs XML `///` docs; `CA1305/CA1307/CA1310` → pass `CultureInfo.InvariantCulture` / `StringComparison.Ordinal`; `CA2007` → `.ConfigureAwait(false)` on awaited tasks.
- Tests are plain xUnit `Assert.*` + NSubstitute. NO FluentAssertions. `using Xunit` is a global using — do not add it per-file. Skippable tests use `Xunit.SkippableFact`.
- `dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx` is a hard CI gate that fails on any diff — run it before every commit (local tool, `dotnet jb`).
- SixLabors versions are pinned exactly: `ImageSharp 3.1.12`, `ImageSharp.Drawing 2.1.7`. Never change either version (v4/v3 are paid-license and hard-fail the build). `PatternPen` and `Pens` live in `SixLabors.ImageSharp.Drawing.Processing`.
- `MarkerIconComposer`, `TunnelTokens`, and other `internal` Map types are visible to `RustPlusBot.Features.Map.Tests` via the existing `InternalsVisibleTo` — tests may reference them directly.
- EF migrations use the local tool: `dotnet ef` (dotnet-ef 10.0.9). Migrations live in `src/RustPlusBot.Persistence/Migrations`; `--startup-project src/RustPlusBot.Host`.
- Docs under `docs/superpowers/**` are gitignored — never `git add` this plan or the spec.
- Icon art is vendored game-derived PNGs copied from `/home/handys11/Dev/rustplusplus/src/resources/images/markers/` (consistent with the existing `cargo.png`).
- Spec: `docs/superpowers/specs/2026-07-11-map-visual-overhaul-design.md`.
- Layer semantics: `MapLayerSettings.Tunnels` defaults **true** (feature on by default); `MapLayerSet.Tunnels` (render instruction) defaults **false** (safe); the composer maps settings→render explicitly, so the asymmetry never leaks.

---

### Task 1: Event icons — MarkerIconComposer (heli 1 rotor, CH47 2 rotors)

Replace the ornate red patrol icon and mis-centered CH47 with grey composites built from body + rotor parts. The CH47 gets **two** rotor blades at the front and rear tandem positions; the heli gets one on its hub. Rotation of the whole composite (existing `DrawMarkers` path) keeps the rotors aligned.

**Files:**
- Copy assets into `src/RustPlusBot.Features.Map/Assets/icons/`:
  - Add `heli.png` (from reference `heli.png`), `chinook.png` (from reference `chinook.png`), `blade.png` (from reference `blade.png`).
  - Overwrite `vendor.png` with the reference `shop.png`.
  - Delete `patrol.png` and `ch47.png`.
- Create: `src/RustPlusBot.Features.Map/Assets/MarkerIconComposer.cs`
- Modify: `src/RustPlusBot.Features.Map/Assets/MapIcons.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/MarkerIconComposerTests.cs` (create), `tests/RustPlusBot.Features.Map.Tests/MapIconsTests.cs` (unchanged — the heli/chinook resolve asserts still pass)

**Interfaces:**
- Produces:
  - `static class MarkerIconComposer` with:
    - `internal static IReadOnlyList<PointF> HeliRotorOffsets { get; }` — 1 entry.
    - `internal static IReadOnlyList<PointF> ChinookRotorOffsets { get; }` — 2 entries, one in the body's top half, one in the bottom half.
    - `internal static Image<Rgba32> Heli()` — heli body with one blade composited; caller owns disposal (but `MapIcons` caches it).
    - `internal static Image<Rgba32> Chinook()` — chinook body with two blades composited.
  - `MapIcons.Marker(MarkerKind)` returns the composite for heli/chinook, unchanged signature.

- [ ] **Step 1: Copy the vendored assets**

```bash
cd /home/handys11/Dev/RustPlusBot
REF=/home/handys11/Dev/rustplusplus/src/resources/images/markers
DST=src/RustPlusBot.Features.Map/Assets/icons
cp "$REF/heli.png" "$DST/heli.png"
cp "$REF/chinook.png" "$DST/chinook.png"
cp "$REF/blade.png" "$DST/blade.png"
cp "$REF/shop.png" "$DST/vendor.png"
git rm "$DST/patrol.png" "$DST/ch47.png"
git add "$DST/heli.png" "$DST/chinook.png" "$DST/blade.png" "$DST/vendor.png"
```

Expected: `heli.png`, `chinook.png`, `blade.png`, `vendor.png` present; `patrol.png`, `ch47.png` gone. (They are picked up automatically by the existing `Assets/icons/*.png` embed glob.)

- [ ] **Step 2: Write the failing test**

Create `tests/RustPlusBot.Features.Map.Tests/MarkerIconComposerTests.cs`:

```csharp
using RustPlusBot.Features.Map.Assets;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MarkerIconComposerTests
{
    [Fact]
    public void Heli_has_one_rotor()
    {
        Assert.Single(MarkerIconComposer.HeliRotorOffsets);
        using var heli = MarkerIconComposer.Heli();
        Assert.True(heli.Width > 0 && heli.Height > 0);
    }

    [Fact]
    public void Chinook_has_two_rotors_placed_apart()
    {
        Assert.Equal(2, MarkerIconComposer.ChinookRotorOffsets.Count);
        using var chinook = MarkerIconComposer.Chinook();

        // One rotor in the top half of the body, one in the bottom half (tandem placement).
        var midY = chinook.Height / 2f;
        Assert.Contains(MarkerIconComposer.ChinookRotorOffsets, p => p.Y < midY);
        Assert.Contains(MarkerIconComposer.ChinookRotorOffsets, p => p.Y > midY);
    }

    [Fact]
    public void Composited_chinook_adds_blade_pixels_to_the_bare_body()
    {
        // The composite must differ from the bare body — proving blades were actually drawn on.
        using var composite = MarkerIconComposer.Chinook();
        using var body = LoadEmbedded("chinook");

        Assert.Equal(body.Width, composite.Width);
        Assert.Equal(body.Height, composite.Height);
        var changed = 0;
        for (var y = 0; y < body.Height; y++)
        {
            for (var x = 0; x < body.Width; x++)
            {
                if (body[x, y] != composite[x, y])
                {
                    changed++;
                }
            }
        }

        Assert.True(changed > 0, "composite identical to bare body — no blades drawn");
    }

    private static Image<Rgba32> LoadEmbedded(string key)
    {
        var asm = typeof(MarkerIconComposer).Assembly;
        using var stream = asm.GetManifestResourceStream($"RustPlusBot.Features.Map.Assets.icons.{key}.png")!;
        return Image.Load<Rgba32>(stream);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MarkerIconComposerTests`
Expected: FAIL — `MarkerIconComposer` does not exist (compile error).

- [ ] **Step 4: Implement `MarkerIconComposer`**

Create `src/RustPlusBot.Features.Map/Assets/MarkerIconComposer.cs`:

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RustPlusBot.Features.Map.Assets;

/// <summary>
/// Builds the patrol-heli and CH47 marker icons by compositing a grey body with rotor blade(s):
/// the heli gets one blade on its hub, the CH47 gets two at its tandem-rotor positions. Blades are
/// drawn into a copy of the body canvas, so the composite keeps the body's dimensions and rotates
/// cleanly about its centre in <see cref="MapRenderer"/>. Offsets/scale are tuned against the
/// reference art (verify visually on a rendered tile).
/// </summary>
internal static class MarkerIconComposer
{
    private const string ResourcePrefix = "RustPlusBot.Features.Map.Assets.icons.";

    // Blade edge as a fraction of the body's shorter edge.
    private const float HeliBladeScale = 1.15f;   // main rotor spans past the fuselage
    private const float ChinookBladeScale = 0.62f; // each tandem rotor is smaller than the body

    /// <summary>The single main-rotor hub offset, in body pixels (populated on first access).</summary>
    internal static IReadOnlyList<PointF> HeliRotorOffsets { get; } = HeliOffsets();

    /// <summary>The two tandem-rotor offsets (front, rear), in body pixels.</summary>
    internal static IReadOnlyList<PointF> ChinookRotorOffsets { get; } = ChinookOffsets();

    /// <summary>Builds the patrol-heli composite (body + one rotor blade on the hub).</summary>
    internal static Image<Rgba32> Heli() =>
        Compose("heli", HeliRotorOffsets, HeliBladeScale);

    /// <summary>Builds the CH47 composite (body + two rotor blades at the tandem positions).</summary>
    internal static Image<Rgba32> Chinook() =>
        Compose("chinook", ChinookRotorOffsets, ChinookBladeScale);

    private static IReadOnlyList<PointF> HeliOffsets()
    {
        using var body = Load("heli");
        // Hub sits slightly forward of centre.
        return [new PointF(body.Width / 2f, body.Height * 0.44f)];
    }

    private static IReadOnlyList<PointF> ChinookOffsets()
    {
        using var body = Load("chinook");
        var cx = body.Width / 2f;
        return
        [
            new PointF(cx, body.Height * 0.20f), // front rotor
            new PointF(cx, body.Height * 0.80f), // rear rotor
        ];
    }

    private static Image<Rgba32> Compose(string bodyKey, IReadOnlyList<PointF> offsets, float bladeScale)
    {
        var canvas = Load(bodyKey); // returned to caller (cached by MapIcons); not disposed here
        using var blade = Load("blade");
        var edge = (int)(Math.Min(canvas.Width, canvas.Height) * bladeScale);
        using var scaledBlade = blade.Clone(c => c.Resize(edge, edge));

        canvas.Mutate(ctx =>
        {
            foreach (var offset in offsets)
            {
                var topLeft = new Point(
                    (int)(offset.X - (scaledBlade.Width / 2f)),
                    (int)(offset.Y - (scaledBlade.Height / 2f)));
                ctx.DrawImage(scaledBlade, topLeft, 1f);
            }
        });

        return canvas;
    }

    private static Image<Rgba32> Load(string key)
    {
        var asm = typeof(MarkerIconComposer).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourcePrefix + key + ".png")
                           ?? throw new InvalidOperationException($"Embedded marker asset '{key}.png' not found.");
        return Image.Load<Rgba32>(stream);
    }
}
```

- [ ] **Step 5: Route heli/chinook through the composer in `MapIcons`**

In `src/RustPlusBot.Features.Map/Assets/MapIcons.cs`, replace the `Load` method (lines 61-66) and update `Scaled` (line 68) to source natives through a new `Native` method that builds composites for the two keys:

```csharp
    private static Image<Rgba32>? Native(string? key) => key is null
        ? null
        : Cache.GetOrAdd(key, static k => k switch
        {
            "patrol" => MarkerIconComposer.Heli(),
            "ch47" => MarkerIconComposer.Chinook(),
            _ => LoadPng(k),
        });

    private static Image<Rgba32>? LoadPng(string key)
    {
        var asm = typeof(MapIcons).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourcePrefix + key + ".png");
        return stream is null ? null : Image.Load<Rgba32>(stream);
    }

    private static Image<Rgba32>? Scaled(string? key, int size) => key is null
        ? null
        : Cache.GetOrAdd($"{key}@{size}", _ =>
        {
            var native = Native(key);
            return native?.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max, Size = new Size(size, size),
            }));
        });
```

Then update the two callers that used `Load`:
- `Marker(MarkerKind kind)` (line 30): `return key is null ? null : Native(key);`
- `Vendor()` (line 41): `public static Image<Rgba32>? Vendor() => Native("vendor");`
- `Player()` (line 45): `public static Image<Rgba32>? Player() => Native("player");`

(`player.png` still exists at this point; it is removed in Task 5.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter "MarkerIconComposerTests|MapIconsTests"`
Expected: PASS (heli/chinook composites resolve; chinook has 2 rotors placed apart).

- [ ] **Step 7: Visual check + reformat**

Run: `dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx` — expect no diff after fixing any formatting. Render a real tile (live `#map` smoke during review) and tune `HeliBladeScale` / `ChinookBladeScale` / the `*Offsets` fractions until the rotors sit correctly; re-run Step 6.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Map/Assets/MarkerIconComposer.cs \
        src/RustPlusBot.Features.Map/Assets/MapIcons.cs \
        src/RustPlusBot.Features.Map/Assets/icons \
        tests/RustPlusBot.Features.Map.Tests/MarkerIconComposerTests.cs
git commit -m "feat(map): composite event icons with correctly-placed rotors"
```

---

### Task 2: Subtle motion trails

Keep trails but make them a faint dashed hint instead of a solid fading line.

**Files:**
- Modify: `src/RustPlusBot.Features.Map/Rendering/MapRenderStyle.cs`
- Modify: `src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs:162-183` (`DrawTrails`)
- Test: `tests/RustPlusBot.Features.Map.Tests/MapRendererTests.cs` (existing `Trail_draws_pixels_between_history_points` still passes)

**Interfaces:**
- Produces: `MapRenderStyle.TrailWidth` reduced; new `MapRenderStyle.TrailMaxAlpha`, `MapRenderStyle.TrailDash` (a `float[]` dash pattern).

- [ ] **Step 1: Adjust the style constants**

In `MapRenderStyle.cs`, change `TrailWidth` and add two constants after it:

```csharp
    /// <summary>Trail polyline stroke width, in output pixels (thin, so it reads as a faint hint).</summary>
    public const float TrailWidth = 1.25f;

    /// <summary>Opacity ceiling for the newest trail segment (older segments fade below this).</summary>
    public const float TrailMaxAlpha = 0.4f;

    /// <summary>Dash pattern (on, off, on, off …) for the trail, in multiples of the pen width.</summary>
    public static float[] TrailDash { get; } = [3f, 3f];
```

- [ ] **Step 2: Draw a dashed, dimmer trail**

Replace the inner loop of `DrawTrails` (`MapRenderer.cs`) so it uses a dashed pen and the lower alpha ceiling:

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
                    // Fade from faint (oldest) to the alpha ceiling (newest) so direction still reads,
                    // but stays subtle. A dashed pen keeps it from looking like a solid smear.
                    var alpha = MapRenderStyle.TrailMaxAlpha * i / (marker.Trail.Count - 1);
                    var pen = new PatternPen(baseColor.WithAlpha(alpha), MapRenderStyle.TrailWidth,
                        MapRenderStyle.TrailDash);
                    ctx.DrawLine(pen, marker.Trail[i - 1], marker.Trail[i]);
                }
            }
        });
    }
```

Add `using SixLabors.ImageSharp.Drawing.Processing;` if not already present (it is — line 7).

- [ ] **Step 3: Run the trail test**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapRendererTests`
Expected: PASS. `Trail_draws_pixels_between_history_points` asserts the changed width spans A→B (> `CargoIconSize*2` = 80px); the dashed line across half the map still spans that far.

- [ ] **Step 4: Reformat + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add src/RustPlusBot.Features.Map/Rendering/MapRenderStyle.cs \
        src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs
git commit -m "feat(map): soften motion trails to a faint dashed hint"
```

---

### Task 3: Tunnels layer — persistence

Add a persisted `Tunnels` toggle (defaults on) to the map settings, mirroring the existing bool layers.

**Files:**
- Modify: `src/RustPlusBot.Domain/Map/ServerMapSettings.cs` (add `ShowTunnels`)
- Modify: `src/RustPlusBot.Persistence/Map/MapLayer.cs` (add `Tunnels = 6` + `MapLayerSettings.Tunnels`)
- Modify: `src/RustPlusBot.Persistence/Map/MapSettingsStore.cs` (read/write the column)
- Modify: `src/RustPlusBot.Features.Workspace/Modules/MapComponentModule.cs:108-117` (`IsEnabled` gains a `Tunnels` case)
- Create: migration `src/RustPlusBot.Persistence/Migrations/*_MapTunnelsLayer.cs` (via `dotnet ef`)
- Test: `tests/RustPlusBot.Persistence.Tests/Map/ServerMapSettingsSchemaTests.cs`, `tests/RustPlusBot.Persistence.Tests/Map/MapSettingsStoreTests.cs`

**Interfaces:**
- Produces:
  - `ServerMapSettings.ShowTunnels : bool` (default `true`).
  - `MapLayer.Tunnels = 6`.
  - `MapLayerSettings(bool Grid, bool Markers, bool Monuments, bool Vendor, bool Players, bool Rigs, bool Tunnels = true, MapGridStyle GridStyle = MapGridStyle.InGame)` — `Tunnels` inserted before `GridStyle`, both defaulted.
  - `MapLayerSettings.Tunnels : bool`; `MapLayerSettings.AllOn` includes `Tunnels: true`.

- [ ] **Step 1: Write the failing persistence tests**

In `ServerMapSettingsSchemaTests.cs`, add to `ServerMapSettings_persists_with_all_layers_on_by_default` after line 35 (`Assert.True(read.ShowRigs);`):

```csharp
        Assert.True(read.ShowTunnels);
```

In `MapSettingsStoreTests.cs`, add a new test:

```csharp
    [Fact]
    public async Task SetLayerAsync_can_disable_tunnels_only()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var server = SeedServer(context);

        await new MapSettingsStore(context).SetLayerAsync(1UL, server.Id, MapLayer.Tunnels, enabled: false);
        var result = await new MapSettingsStore(context).GetAsync(1UL, server.Id);

        Assert.False(result.Tunnels);
        Assert.True(result.Monuments); // other layers untouched
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter Map`
Expected: FAIL — `ShowTunnels` / `MapLayer.Tunnels` / `result.Tunnels` do not exist (compile errors).

- [ ] **Step 3: Add the entity column**

In `src/RustPlusBot.Domain/Map/ServerMapSettings.cs`, add after `ShowRigs` (line ~31):

```csharp
    /// <summary>Whether train-tunnel entrance icons are drawn (separate from monuments).</summary>
    public bool ShowTunnels { get; set; } = true;
```

- [ ] **Step 4: Extend `MapLayer` + `MapLayerSettings`**

In `src/RustPlusBot.Persistence/Map/MapLayer.cs`, add the enum value after `Rigs = 5`:

```csharp
    /// <summary>The train-tunnel entrance layer.</summary>
    Tunnels = 6,
```

Change the `MapLayerSettings` record header to insert `Tunnels` before `GridStyle`:

```csharp
public sealed record MapLayerSettings(
    bool Grid,
    bool Markers,
    bool Monuments,
    bool Vendor,
    bool Players,
    bool Rigs,
    bool Tunnels = true,
    MapGridStyle GridStyle = MapGridStyle.InGame)
{
    /// <summary>All layers enabled — the default when no settings row exists.</summary>
    public static MapLayerSettings AllOn { get; } = new(true, true, true, true, true, true);
}
```

Add the doc-comment line for the new param under the record's `<param>` list:

```csharp
/// <param name="Tunnels">Train-tunnel entrance icons.</param>
```

(`AllOn` needs no argument change — `Tunnels` defaults true.)

- [ ] **Step 5: Read/write the column in `MapSettingsStore`**

In `MapSettingsStore.cs`, update the `GetAsync` projection (line 21-22) to include `ShowTunnels` **before** `GridStyle`:

```csharp
            : new MapLayerSettings(row.ShowGrid, row.ShowMarkers, row.ShowMonuments, row.ShowVendor,
                row.ShowPlayers, row.ShowRigs, row.ShowTunnels, row.GridStyle);
```

Add a `case` to the `SetLayerAsync` switch (before `default:`, line ~59):

```csharp
            case MapLayer.Tunnels:
                row.ShowTunnels = enabled;
                break;
```

- [ ] **Step 6: Add the `Tunnels` case to the module `IsEnabled`**

In `MapComponentModule.cs`, add to the `IsEnabled` switch (before `_ => true`, line ~115):

```csharp
        MapLayer.Tunnels => settings.Tunnels,
```

- [ ] **Step 7: Generate the migration**

Run:

```bash
dotnet ef migrations add MapTunnelsLayer \
  --project src/RustPlusBot.Persistence \
  --startup-project src/RustPlusBot.Host
```

Then open the generated `src/RustPlusBot.Persistence/Migrations/*_MapTunnelsLayer.cs` and ensure the `Up` uses `defaultValue: true` (EF emits `false` for bool by default — change it so existing rows keep the all-on convention). It must read:

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowTunnels",
                table: "ServerMapSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowTunnels",
                table: "ServerMapSettings");
        }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter Map`
Expected: PASS (`ShowTunnels` defaults true, `SetLayerAsync(Tunnels,false)` round-trips).

- [ ] **Step 9: Reformat + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add src/RustPlusBot.Domain/Map/ServerMapSettings.cs \
        src/RustPlusBot.Persistence/Map/MapLayer.cs \
        src/RustPlusBot.Persistence/Map/MapSettingsStore.cs \
        src/RustPlusBot.Persistence/Migrations \
        src/RustPlusBot.Features.Workspace/Modules/MapComponentModule.cs \
        tests/RustPlusBot.Persistence.Tests/Map
git commit -m "feat(map): persist a Tunnels layer toggle (defaults on)"
```

---

### Task 4a: Tunnels layer — render routing

Split the two train-tunnel tokens out of the Monuments layer into their own render pass, gated by `MapLayerSet.Tunnels`. Icons stay the RustMaps SVG art (reused via `MonumentIconSource`).

**Files:**
- Modify: `src/RustPlusBot.Features.Map/Rendering/MapLayerSet.cs` (add `Tunnels`)
- Create: `src/RustPlusBot.Features.Map/Assets/TunnelTokens.cs`
- Modify: `src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs` (`Render` gains a `tunnels` param + a draw pass)
- Modify: `src/RustPlusBot.Features.Map/Composing/MapComposer.cs` (exclude in `GatherMonuments`, add `GatherTunnels`, pass to `Render`)
- Test: `tests/RustPlusBot.Features.Map.Tests/MapLayerSetTests.cs`, `MapRendererTests.cs`, `MapComposerTests.cs`

**Interfaces:**
- Consumes: `MapLayerSettings.Tunnels` (Task 3), `MonumentIconSource.Monument(string, int)`.
- Produces:
  - `MapLayerSet(bool Grid, bool Markers, bool Monuments, bool Vendor, bool Players, bool Rigs, bool Tunnels = false)`; `MapLayerSet.AllOn` includes `Tunnels: true`.
  - `static class TunnelTokens { static IReadOnlySet<string> All { get; } }` = `{ "train_tunnel_display_name", "train_tunnel_link_display_name" }`.
  - `MapRenderer.Render(..., MapLayerSet layers, MapGridStyle gridStyle = InGame, IReadOnlyList<MonumentPlacement>? tunnels = null)`.

- [ ] **Step 1: Write failing tests**

In `MapLayerSetTests.cs`, add to `AllOn_enables_every_layer` after `Assert.True(set.Rigs);`:

```csharp
        Assert.True(set.Tunnels);
```

In `MapComposerTests.cs`, add a test that the tunnel tokens route to the tunnels pass, not monuments:

```csharp
    [Fact]
    public async Task Tunnel_tokens_render_only_when_the_tunnels_layer_is_on()
    {
        // Two monuments at the same spot: a launchsite (ordinary) and a tunnel entrance.
        var query = NewQuery();
        query.GetMonumentsAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns([new MonumentSnapshot("train_tunnel_display_name", 2000f, 2000f)]);
        var events = NewEvents();

        // Monuments ON, Tunnels OFF -> the tunnel token must NOT draw (it belongs to the Tunnels layer).
        var tunnelsOff = Build(BaseJpeg(), Dims, query, events, NewRigs(),
            NewSettings(MapLayerSettings.AllOn with { Tunnels = false }));
        // Monuments OFF, Tunnels ON -> the tunnel token draws via the tunnels pass.
        var query2 = NewQuery();
        query2.GetMonumentsAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns([new MonumentSnapshot("train_tunnel_display_name", 2000f, 2000f)]);
        var tunnelsOn = Build(BaseJpeg(), Dims, query2, events, NewRigs(),
            NewSettings(new MapLayerSettings(
                Grid: false, Markers: false, Monuments: false, Vendor: false, Players: false, Rigs: false,
                Tunnels: true)));

        var off = await tunnelsOff.ComposeAsync(Guild, Server, CancellationToken.None);
        var on = await tunnelsOn.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(off);
        Assert.NotNull(on);
        // With Monuments on but Tunnels off, the tunnel token is excluded from the monuments pass, so the
        // only difference from a Tunnels-on/Monuments-off render is whether the tunnel icon was painted.
        Assert.False(off!.SequenceEqual(on!), "tunnel token must render under the Tunnels layer only");
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter "MapLayerSetTests|MapComposerTests"`
Expected: FAIL — `set.Tunnels` / `MapLayerSettings ... Tunnels` compile errors.

- [ ] **Step 3: Add `Tunnels` to `MapLayerSet`**

Replace `MapLayerSet.cs` body:

```csharp
namespace RustPlusBot.Features.Map.Rendering;

/// <summary>Which overlay layers the renderer should draw.</summary>
/// <param name="Grid">Draw the map grid lines.</param>
/// <param name="Markers">Draw live cargo/heli/chinook markers.</param>
/// <param name="Monuments">Draw monument icons.</param>
/// <param name="Vendor">Draw the travelling-vendor marker.</param>
/// <param name="Players">Draw teammate position markers.</param>
/// <param name="Rigs">Style oil rigs by activation state.</param>
/// <param name="Tunnels">Draw train-tunnel entrance icons.</param>
public sealed record MapLayerSet(
    bool Grid,
    bool Markers,
    bool Monuments,
    bool Vendor,
    bool Players,
    bool Rigs,
    bool Tunnels = false)
{
    /// <summary>All layers enabled — the defaults-on render.</summary>
    public static MapLayerSet AllOn { get; } = new(true, true, true, true, true, true, true);
}
```

- [ ] **Step 4: Add the `TunnelTokens` constant**

Create `src/RustPlusBot.Features.Map/Assets/TunnelTokens.cs`:

```csharp
namespace RustPlusBot.Features.Map.Assets;

/// <summary>
/// The Rust+ monument tokens that belong to the separately-toggleable Tunnels layer rather than the
/// general Monuments layer. Trainyard and Military Tunnels stay ordinary monuments.
/// </summary>
public static class TunnelTokens
{
    /// <summary>The train-tunnel entrance and link tokens.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "train_tunnel_display_name",
        "train_tunnel_link_display_name",
    };
}
```

- [ ] **Step 5: Add the tunnels draw pass to `MapRenderer`**

In `MapRenderer.cs`, add the `tunnels` parameter to `Render` (after `gridStyle`) and a draw pass after monuments. Change the signature:

```csharp
    public byte[] Render(byte[] baseJpeg,
        MapProjection projection,
        IReadOnlyList<MarkerPlacement> markers,
        IReadOnlyList<MonumentPlacement> monuments,
        IReadOnlyList<PlayerPlacement> players,
        IReadOnlyList<RigPlacement> rigs,
        MapLayerSet layers,
        MapGridStyle gridStyle = MapGridStyle.InGame,
        IReadOnlyList<MonumentPlacement>? tunnels = null)
```

Add the null-guard is unnecessary (null means empty); after the `if (layers.Monuments) DrawMonuments(...)` block, add:

```csharp
        if (layers.Tunnels && tunnels is { Count: > 0 })
        {
            DrawMonuments(image, tunnels);
        }
```

(`DrawMonuments` already takes any `IReadOnlyList<MonumentPlacement>` and resolves icons via `MonumentIconSource.Monument` — tunnel tokens keep their existing mapping, so the icons are identical.)

- [ ] **Step 6: Route tunnels in `MapComposer`**

In `MapComposer.cs`:

Exclude tunnel tokens from `GatherMonuments` (line 152 loop) — change the `foreach` body guard:

```csharp
        if (layers.Monuments)
        {
            foreach (var mon in serverMonuments)
            {
                if (TunnelTokens.All.Contains(mon.Token))
                {
                    continue; // routed to the Tunnels layer instead
                }

                var (px, py) = projection.ToPixel(mon.X, mon.Y);
                monuments.Add(new MonumentPlacement(mon.Token, px, py));
            }
        }
```

Add a `GatherTunnels` method next to `GatherMonuments`:

```csharp
    private static List<MonumentPlacement> GatherTunnels(
        IReadOnlyList<MonumentSnapshot> serverMonuments,
        MapProjection projection,
        MapLayerSet layers)
    {
        var tunnels = new List<MonumentPlacement>();
        if (layers.Tunnels)
        {
            foreach (var mon in serverMonuments.Where(m => TunnelTokens.All.Contains(m.Token)))
            {
                var (px, py) = projection.ToPixel(mon.X, mon.Y);
                tunnels.Add(new MonumentPlacement(mon.Token, px, py));
            }
        }

        return tunnels;
    }
```

Add `using RustPlusBot.Features.Map.Assets;` to the file's usings.

Update the monument-fetch gate (line 85) so tunnels also trigger the fetch, and build+pass the tunnel list. Replace lines 84-96:

```csharp
        // Monuments feed the monuments, rig-styling, and tunnels layers; fetch them once when any is on.
        IReadOnlyList<MonumentSnapshot> serverMonuments = [];
        if (layers.Monuments || layers.Rigs || layers.Tunnels)
        {
            serverMonuments = await query.GetMonumentsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        }

        var monuments = GatherMonuments(serverMonuments, projection, layers);
        var tunnels = GatherTunnels(serverMonuments, projection, layers);
        var players = await GatherPlayersAsync(guildId, serverId, projection, layers, cancellationToken)
            .ConfigureAwait(false);
        var rigPlacements = GatherRigs(guildId, serverId, serverMonuments, projection, layers);

        return renderer.Render(baseImage.Bytes, projection, markers, monuments, players, rigPlacements, layers,
            gridStyle, tunnels);
```

Update the `MapLayerSet` construction (line 47) to pass `settings.Tunnels`:

```csharp
        var layers = new MapLayerSet(settings.Grid, settings.Markers, settings.Monuments,
            settings.Vendor, settings.Players, settings.Rigs, settings.Tunnels);
```

- [ ] **Step 7: Fix the existing `ComposeAsync_renders_only_enabled_layers` test**

That test (`MapComposerTests.cs:131`) sets `Monuments: false, ... Rigs: false` and asserts `GetMonumentsAsync` is **not** called. With `MapLayerSettings.Tunnels` now defaulting **true**, the monument fetch would now run (tunnels need it). Set `Tunnels: false` in that settings literal so the assertion holds:

```csharp
        var settings = NewSettings(new MapLayerSettings(
            Grid: true, Markers: true, Monuments: false, Vendor: true, Players: true, Rigs: false, Tunnels: false));
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter "MapLayerSetTests|MapComposerTests|MapRendererTests"`
Expected: PASS.

- [ ] **Step 9: Reformat + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add src/RustPlusBot.Features.Map/Rendering/MapLayerSet.cs \
        src/RustPlusBot.Features.Map/Assets/TunnelTokens.cs \
        src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs \
        src/RustPlusBot.Features.Map/Composing/MapComposer.cs \
        tests/RustPlusBot.Features.Map.Tests
git commit -m "feat(map): render train tunnels as a separate toggleable layer"
```

---

### Task 4b: Tunnels layer — control-message toggle + localization

Add the 7th toggle button and its strings.

**Files:**
- Modify: `src/RustPlusBot.Features.Workspace/Messages/MapControlMessageRenderer.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `src/RustPlusBot.Localization/Strings.fr.resx`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Messages/MapControlMessageRendererTests.cs`, `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs`

**Interfaces:**
- Consumes: `MapLayer.Tunnels`, `MapLayerSettings.Tunnels`, localization key `map.layer.tunnels`.

- [ ] **Step 1: Add the localization strings**

In `src/RustPlusBot.Localization/Strings.resx`, add after the `map.layer.rigs` block (keeping alphabetical-ish order near the others):

```xml
  <data name="map.layer.tunnels" xml:space="preserve">
    <value>Tunnels</value>
  </data>
```

In `src/RustPlusBot.Localization/Strings.fr.resx`, add the French value:

```xml
  <data name="map.layer.tunnels" xml:space="preserve">
    <value>Tunnels</value>
  </data>
```

- [ ] **Step 2: Update the parity key-count test (267 → 268)**

In `StringsResourceParityTests.cs:44`, change:

```csharp
        Assert.Equal(268, EnglishKeys().Count);
```

- [ ] **Step 3: Update the control-message toggle-count test (6 → 7)**

In `MapControlMessageRendererTests.cs`, update `Renders_six_toggle_buttons_reflecting_settings`: rename to `Renders_seven_toggle_buttons_reflecting_settings`, keep the settings literal at 6 positional bools (Tunnels defaults true), and change the count assertion (line 38):

```csharp
        Assert.Equal(7, toggles.Count);
```

The Monuments-off assertion still holds (Monuments is the only `false`); the `foreach (other …) Assert.Equal(Success)` still holds because Tunnels defaults true.

- [ ] **Step 4: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests tests/RustPlusBot.Localization.Tests --filter "MapControlMessageRendererTests|StringsResourceParityTests"`
Expected: FAIL — only 6 toggle buttons are rendered / key count is 267.

- [ ] **Step 5: Add the toggle button**

In `MapControlMessageRenderer.cs`, add the Tunnels toggle to row 1 (which currently holds Vendor/Players/Rigs — four buttons is within Discord's five-per-row cap). After the `Rigs` toggle (line 41):

```csharp
        AddToggle(builder, MapLayer.Tunnels, "map.layer.tunnels", settings.Tunnels, serverId, context.Culture,
            row: 1);
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests tests/RustPlusBot.Localization.Tests`
Expected: PASS (7 toggles; 268 keys; FR/EN parity).

- [ ] **Step 7: Reformat + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add src/RustPlusBot.Features.Workspace/Messages/MapControlMessageRenderer.cs \
        src/RustPlusBot.Localization/Strings.resx \
        src/RustPlusBot.Localization/Strings.fr.resx \
        tests/RustPlusBot.Features.Workspace.Tests/Messages/MapControlMessageRendererTests.cs \
        tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs
git commit -m "feat(map): add the Tunnels layer toggle button + localization"
```

---

### Task 5: Player palette + colored crosses

Replace the green player circle with a `+`/`x` cross in a stable per-player color; drop the on-map name label.

**Files:**
- Create: `src/RustPlusBot.Features.Map/Rendering/PlayerPalette.cs`
- Modify: `src/RustPlusBot.Features.Map/Rendering/PlayerPlacement.cs` (add `CrossColor`)
- Modify: `src/RustPlusBot.Features.Map/Rendering/MapRenderStyle.cs` (cross constants)
- Modify: `src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs` (`DrawPlayers` → crosses; remove `DrawPlayerLabel`, `PlayerLabelOffset`, `Font`)
- Modify: `src/RustPlusBot.Features.Map/Assets/MapIcons.cs` (remove `Player`/`Player(int)`); delete `Assets/icons/player.png`
- Modify: `src/RustPlusBot.Features.Map/Composing/MapComposer.cs` (`GatherPlayersAsync` assigns palette by SteamId)
- Test: `tests/RustPlusBot.Features.Map.Tests/MapRendererTests.cs`, `MapIconsTests.cs`, `PlayerPaletteTests.cs` (create)

**Interfaces:**
- Produces:
  - `readonly record struct PlayerColor(SixLabors.ImageSharp.Color Rgba, string Emoji)`.
  - `static class PlayerPalette { static IReadOnlyList<PlayerColor> Entries { get; } // 9; static PlayerColor For(int index); }` — `For` wraps modulo 9.
  - `PlayerPlacement(string Name, float PixelX, float PixelY, bool IsAlive, bool IsOnline, Color CrossColor)`.

- [ ] **Step 1: Write failing palette + renderer tests**

Create `tests/RustPlusBot.Features.Map.Tests/PlayerPaletteTests.cs`:

```csharp
using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map.Tests;

public sealed class PlayerPaletteTests
{
    [Fact]
    public void Has_nine_distinct_colors_with_emoji()
    {
        Assert.Equal(9, PlayerPalette.Entries.Count);
        Assert.Equal(9, PlayerPalette.Entries.Select(e => e.Emoji).Distinct().Count());
        Assert.All(PlayerPalette.Entries, e => Assert.False(string.IsNullOrEmpty(e.Emoji)));
    }

    [Fact]
    public void For_wraps_past_the_palette_length()
    {
        Assert.Equal(PlayerPalette.For(0), PlayerPalette.For(9));
        Assert.Equal(PlayerPalette.For(1), PlayerPalette.For(10));
    }
}
```

In `MapRendererTests.cs`, update the two `PlayerPlacement` uses and add cross tests. First, the existing `Render_with_all_layers_produces_valid_png` player (line 103) gains a color:

```csharp
        var players = new[]
        {
            new PlayerPlacement("Alice", 300, 300, IsAlive: true, IsOnline: true,
                PlayerPalette.For(0).Rgba)
        };
```

Add:

```csharp
    [Fact]
    public void Player_cross_paints_in_the_assigned_color()
    {
        var renderer = CreateRenderer();
        var projection = new MapProjection(4000, 2000, 2000, 100, MapRenderer.OutputSize);
        var baseJpeg = SolidJpeg(2000);
        var (px, py) = projection.ToPixel(2000f, 2000f);
        var layers = new MapLayerSet(false, false, false, false, true, false);
        var red = SixLabors.ImageSharp.Color.ParseHex("E03131");

        var without = renderer.Render(baseJpeg, projection, [], [], [], [], layers);
        var with = renderer.Render(baseJpeg, projection, [], [],
            [new PlayerPlacement("A", px, py, IsAlive: true, IsOnline: true, red)], [], layers);

        Assert.False(without.AsSpan().SequenceEqual(with));
        // A red-dominant pixel must appear where the cross was drawn.
        using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(with);
        var found = false;
        for (var dy = -8; dy <= 8 && !found; dy++)
        {
            for (var dx = -8; dx <= 8 && !found; dx++)
            {
                var p = img[(int)px + dx, (int)py + dy];
                if (p.R > 150 && p.G < 120 && p.B < 120)
                {
                    found = true;
                }
            }
        }

        Assert.True(found, "no red cross pixel near the player position");
    }

    [Fact]
    public void Alive_and_dead_players_render_differently()
    {
        var renderer = CreateRenderer();
        var projection = new MapProjection(4000, 2000, 2000, 100, MapRenderer.OutputSize);
        var baseJpeg = SolidJpeg(2000);
        var (px, py) = projection.ToPixel(2000f, 2000f);
        var layers = new MapLayerSet(false, false, false, false, true, false);
        var blue = SixLabors.ImageSharp.Color.ParseHex("1971C2");

        var alive = renderer.Render(baseJpeg, projection, [], [],
            [new PlayerPlacement("A", px, py, IsAlive: true, IsOnline: true, blue)], [], layers);
        var dead = renderer.Render(baseJpeg, projection, [], [],
            [new PlayerPlacement("A", px, py, IsAlive: false, IsOnline: true, blue)], [], layers);

        Assert.False(alive.AsSpan().SequenceEqual(dead)); // '+' vs 'x'
    }
```

In `MapIconsTests.cs`, remove `Player_resolves_to_vendored_icon` and retarget the cache test to a marker (player icon is gone):

```csharp
    [Fact]
    public void Sized_icons_are_cached_per_size()
    {
        Assert.Same(MapIcons.Marker(MarkerKind.CargoShip, 40), MapIcons.Marker(MarkerKind.CargoShip, 40));
        Assert.NotSame(MapIcons.Marker(MarkerKind.CargoShip, 40), MapIcons.Marker(MarkerKind.CargoShip, 44));
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter "PlayerPaletteTests|MapRendererTests|MapIconsTests"`
Expected: FAIL — `PlayerPalette` / the 6-arg `PlayerPlacement` do not exist.

- [ ] **Step 3: Add `PlayerPalette`**

Create `src/RustPlusBot.Features.Map/Rendering/PlayerPalette.cs`:

```csharp
using SixLabors.ImageSharp;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>One palette entry: the on-map cross color and the matching Discord legend square.</summary>
/// <param name="Rgba">The cross color drawn on the map.</param>
/// <param name="Emoji">The colored-square emoji used for this color in the legend.</param>
public readonly record struct PlayerColor(Color Rgba, string Emoji);

/// <summary>
/// The fixed nine-color player palette. Each color's RGB matches its Discord colored-square emoji so
/// an on-map cross and its legend row read as the same color. Players are assigned by stable index
/// (see the composer), wrapping past nine.
/// </summary>
public static class PlayerPalette
{
    /// <summary>The palette, ordered for maximum distinctiveness in the first slots.</summary>
    public static IReadOnlyList<PlayerColor> Entries { get; } =
    [
        new(Color.ParseHex("E03131"), "🟥"), // red
        new(Color.ParseHex("1971C2"), "🟦"), // blue
        new(Color.ParseHex("2F9E44"), "🟩"), // green
        new(Color.ParseHex("F2CC0C"), "🟨"), // yellow
        new(Color.ParseHex("9C36B5"), "🟪"), // purple
        new(Color.ParseHex("E8590C"), "🟧"), // orange
        new(Color.ParseHex("8B5E34"), "🟫"), // brown
        new(Color.ParseHex("212529"), "⬛"),       // black
        new(Color.ParseHex("F1F3F5"), "⬜"),       // white
    ];

    /// <summary>Gets the palette entry for a zero-based player index, wrapping past the palette length.</summary>
    /// <param name="index">The stable zero-based player index.</param>
    /// <returns>The palette entry.</returns>
    public static PlayerColor For(int index) => Entries[((index % Entries.Count) + Entries.Count) % Entries.Count];
}
```

- [ ] **Step 4: Extend `PlayerPlacement` + cross style constants**

In `PlayerPlacement.cs`, add `CrossColor` (and its `<param>`):

```csharp
/// <param name="CrossColor">The palette color the on-map cross is drawn in.</param>
public sealed record PlayerPlacement(
    string Name,
    float PixelX,
    float PixelY,
    bool IsAlive,
    bool IsOnline,
    SixLabors.ImageSharp.Color CrossColor);
```

In `MapRenderStyle.cs`, replace `PlayerIconSize` with cross constants:

```csharp
    /// <summary>Player cross half-arm length, in output pixels.</summary>
    public const float PlayerCrossArm = 7f;

    /// <summary>Player cross colored-stroke width, in output pixels.</summary>
    public const float PlayerCrossWidth = 2.5f;

    /// <summary>Player cross dark-halo stroke width (drawn under the color for contrast).</summary>
    public const float PlayerCrossHaloWidth = 4.5f;

    /// <summary>Opacity for offline players' crosses.</summary>
    public const float PlayerOfflineAlpha = 0.5f;
```

- [ ] **Step 5: Rewrite `DrawPlayers` as crosses**

In `MapRenderer.cs`, replace `DrawPlayers`, `DrawPlayerIcon`, `DrawPlayerLabel` (lines 254-293) with:

```csharp
    private static void DrawPlayers(Image<Rgba32> image, IReadOnlyList<PlayerPlacement> players)
    {
        image.Mutate(ctx =>
        {
            foreach (var player in players)
            {
                DrawPlayerCross(ctx, player);
            }
        });
    }

    private static void DrawPlayerCross(IImageProcessingContext ctx, PlayerPlacement player)
    {
        var arm = MapRenderStyle.PlayerCrossArm;
        var alpha = player.IsOnline ? 1f : MapRenderStyle.PlayerOfflineAlpha;
        var color = player.CrossColor.WithAlpha(alpha);
        var halo = Color.FromRgba(0, 0, 0, (byte)(180 * alpha));

        // Alive = '+', dead = 'x'.
        var (a1, a2, b1, b2) = player.IsAlive
            ? (new PointF(player.PixelX - arm, player.PixelY), new PointF(player.PixelX + arm, player.PixelY),
                new PointF(player.PixelX, player.PixelY - arm), new PointF(player.PixelX, player.PixelY + arm))
            : (new PointF(player.PixelX - arm, player.PixelY - arm),
                new PointF(player.PixelX + arm, player.PixelY + arm),
                new PointF(player.PixelX - arm, player.PixelY + arm),
                new PointF(player.PixelX + arm, player.PixelY - arm));

        // Halo first (wider, dark), then the colored strokes on top.
        ctx.DrawLine(halo, MapRenderStyle.PlayerCrossHaloWidth, a1, a2);
        ctx.DrawLine(halo, MapRenderStyle.PlayerCrossHaloWidth, b1, b2);
        ctx.DrawLine(color, MapRenderStyle.PlayerCrossWidth, a1, a2);
        ctx.DrawLine(color, MapRenderStyle.PlayerCrossWidth, b1, b2);
    }
```

Remove now-unused members from `MapRenderer.cs`: `PlayerLabelOffset` (line 25), the `Font` field + `Family.CreateFont(12f)` (line 28), and — if `Family`/`GridLabelFont`/`LoadFamily` are still used by the grid, keep them; only remove `Font` and `PlayerLabelOffset`. (`GridLabelFont` and `LoadFamily` remain used by `DrawGrid`.) Remove the now-unused `using SixLabors.Fonts;`? No — `GridLabelFont` still needs it. Leave the fonts infrastructure; only delete the `Font` field and `PlayerLabelOffset` const.

- [ ] **Step 6: Remove the player PNG icon**

In `MapIcons.cs`, delete the `Player()` and `Player(int size)` methods (lines 43-50). Delete the asset:

```bash
git rm src/RustPlusBot.Features.Map/Assets/icons/player.png
```

- [ ] **Step 7: Assign palette colors in the composer**

In `MapComposer.cs`, rewrite `GatherPlayersAsync` to sort by SteamId and assign palette colors:

```csharp
    private async Task<List<PlayerPlacement>> GatherPlayersAsync(
        ulong guildId,
        Guid serverId,
        MapProjection projection,
        MapLayerSet layers,
        CancellationToken cancellationToken)
    {
        var players = new List<PlayerPlacement>();
        if (layers.Players)
        {
            var team = await query.GetTeamInfoAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            // Stable color per player: order by SteamId, index into the palette. SteamId never changes,
            // so a player keeps their color across refreshes regardless of online/offline ordering.
            var ordered = (team?.Members ?? []).OrderBy(m => m.SteamId).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var member = ordered[i];
                var (px, py) = projection.ToPixel(member.X, member.Y);
                players.Add(new PlayerPlacement(member.Name, px, py, member.IsAlive, member.IsOnline,
                    PlayerPalette.For(i).Rgba));
            }
        }

        return players;
    }
```

Add `using System.Linq;` if not already implied (global usings usually cover it; verify build).

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests`
Expected: PASS (crosses paint in color; alive/dead differ; palette wraps; no player.png needed).

- [ ] **Step 9: Reformat + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add src/RustPlusBot.Features.Map/Rendering/PlayerPalette.cs \
        src/RustPlusBot.Features.Map/Rendering/PlayerPlacement.cs \
        src/RustPlusBot.Features.Map/Rendering/MapRenderStyle.cs \
        src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs \
        src/RustPlusBot.Features.Map/Assets/MapIcons.cs \
        src/RustPlusBot.Features.Map/Assets/icons/player.png \
        src/RustPlusBot.Features.Map/Composing/MapComposer.cs \
        tests/RustPlusBot.Features.Map.Tests
git commit -m "feat(map): draw players as stable palette-colored crosses, drop name labels"
```

---

### Task 6: Legend model + composition result

Build the color→player→status legend in the composer from the same palette assignment, and return it alongside the PNG.

**Files:**
- Create: `src/RustPlusBot.Features.Map/Composing/MapLegend.cs` (`MapLegend`, `MapLegendEntry`, `MapComposition`)
- Modify: `src/RustPlusBot.Features.Map/Composing/MapComposer.cs` (build legend; `ComposeAsync` returns `MapComposition?`)
- Modify: `src/RustPlusBot.Features.Map/Hosting/MapHostedService.cs:194-200` (consume `.Png` / `.Legend`)
- Test: `tests/RustPlusBot.Features.Map.Tests/MapComposerTests.cs` (update call sites + add legend test)

**Interfaces:**
- Produces:
  - `sealed record MapLegendEntry(string Emoji, string Name, string Status)`.
  - `sealed record MapLegend(IReadOnlyList<MapLegendEntry> Entries)`.
  - `sealed record MapComposition(byte[] Png, MapLegend? Legend)`.
  - `MapComposer.ComposeAsync(...) : Task<MapComposition?>`.

- [ ] **Step 1: Write the failing legend test**

In `MapComposerTests.cs`, add:

```csharp
    [Fact]
    public async Task ComposeAsync_builds_a_legend_entry_per_player()
    {
        var query = NewQuery();
        query.GetTeamInfoAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(0,
            [
                new TeamMemberSnapshot(20, "Bob", 2000f, 2000f, IsOnline: false, IsAlive: false, default, default),
                new TeamMemberSnapshot(10, "Ada", 2000f, 2000f, IsOnline: true, IsAlive: true, default, default),
            ]));
        var composer = Build(BaseJpeg(), Dims, query, NewEvents(), NewRigs(),
            NewSettings(MapLayerSettings.AllOn));

        var result = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.Legend);
        // Ordered by SteamId: Ada (10) first, Bob (20) second.
        Assert.Collection(result.Legend!.Entries,
            e => { Assert.Equal("Ada", e.Name); Assert.Equal("online", e.Status); },
            e => { Assert.Equal("Bob", e.Name); Assert.Equal("offline, dead", e.Status); });
        // Ada gets palette[0], Bob palette[1].
        Assert.Equal(PlayerPalette.For(0).Emoji, result.Legend.Entries[0].Emoji);
        Assert.Equal(PlayerPalette.For(1).Emoji, result.Legend.Entries[1].Emoji);
    }
```

- [ ] **Step 2: Update all existing `ComposeAsync` call sites in `MapComposerTests.cs`**

Every `var png = await …ComposeAsync(…)` must become the composition result. Apply these exact edits:

- `ComposeAsync_returns_null_when_no_base_map` (lines 86-88):
  ```csharp
        var result = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.Null(result);
  ```
- `Renders_a_png_when_base_map_available` (lines 99-103):
  ```csharp
        var result = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(result);
        using var image = Image.Load<Rgba32>(result!.Png);
        Assert.Equal(MapRenderer.OutputSize, image.Width);
  ```
- `Renders_base_only_when_dimensions_unavailable` (lines 114-118):
  ```csharp
        var result = await composer.ComposeAsync(Guild, Server, CancellationToken.None);

        Assert.NotNull(result);
        using var image = Image.Load<Rgba32>(result!.Png);
        Assert.Equal(MapRenderer.OutputSize, image.Width);
  ```
- `ComposeAsync_renders_only_enabled_layers` (line 136-138): `var result = …; Assert.NotNull(result);`
- `ComposeAsync_uses_all_on_when_no_settings_row` (line 160-162): `var result = …; Assert.NotNull(result);`
- `Renders_the_grid_even_with_no_markers` (lines 182-188): rename locals to `resultOn`/`resultOff`, then:
  ```csharp
        Assert.NotNull(resultOn);
        Assert.NotNull(resultOff);
        Assert.False(resultOn!.Png.SequenceEqual(resultOff!.Png), "Grid-on and grid-off renders must differ.");
  ```
- `ComposeAsync_forwards_vendor_marker_history_into_rendered_trail` (lines 209-217): rename to `resultWithTrail`/`resultNoTrail`, then:
  ```csharp
        Assert.NotNull(resultWithTrail);
        Assert.NotNull(resultNoTrail);
        Assert.False(resultWithTrail!.Png.SequenceEqual(resultNoTrail!.Png),
            "Vendor trail with 2-point history must render differently than a 1-point history.");
  ```
- `Tunnel_tokens_render_only_when_the_tunnels_layer_is_on` (Task 4a): change `off`/`on` to `.Png`:
  ```csharp
        Assert.NotNull(off);
        Assert.NotNull(on);
        Assert.False(off!.Png.SequenceEqual(on!.Png), "tunnel token must render under the Tunnels layer only");
  ```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapComposerTests`
Expected: FAIL — `MapComposition` / `ComposeAsync` return type don't exist yet.

- [ ] **Step 4: Add the legend + composition records**

Create `src/RustPlusBot.Features.Map/Composing/MapLegend.cs`:

```csharp
namespace RustPlusBot.Features.Map.Composing;

/// <summary>One legend row: a colored square, the player's name, and their status.</summary>
/// <param name="Emoji">The colored-square emoji matching the player's on-map cross color.</param>
/// <param name="Name">The player's in-game display name.</param>
/// <param name="Status">Human-readable status, e.g. "online" or "offline, dead".</param>
public sealed record MapLegendEntry(string Emoji, string Name, string Status);

/// <summary>The player legend for a rendered map, one entry per teammate.</summary>
/// <param name="Entries">The legend rows, ordered as the crosses were assigned (by SteamId).</param>
public sealed record MapLegend(IReadOnlyList<MapLegendEntry> Entries);

/// <summary>A rendered map: the PNG plus its optional player legend.</summary>
/// <param name="Png">The rendered PNG bytes.</param>
/// <param name="Legend">The player legend, or null when the Players layer is off or the team is empty.</param>
public sealed record MapComposition(byte[] Png, MapLegend? Legend);
```

- [ ] **Step 5: Return `MapComposition` and build the legend in the composer**

In `MapComposer.cs`:

Change `ComposeAsync` (line 35) and `ComposeWithLayersAsync` (line 53) return types to `Task<MapComposition?>`.

At the `baseImage is null` guard (line 62), return `null` (unchanged).

At the dims-unavailable early return (lines 72-75), wrap the PNG:

```csharp
            return new MapComposition(
                renderer.Render(baseImage.Bytes, new MapProjection(0, 1, 1, 0, MapRenderer.OutputSize),
                    markers: [], monuments: [], players: [], rigs: [],
                    new MapLayerSet(Grid: false, Markers: false, Monuments: false, Vendor: false, Players: false,
                        Rigs: false, Tunnels: false)),
                Legend: null);
```

Refactor `GatherPlayersAsync` to also produce the legend. Replace it with an assignment helper + two builders:

```csharp
    private async Task<(List<PlayerPlacement> Players, MapLegend? Legend)> GatherPlayersAsync(
        ulong guildId,
        Guid serverId,
        MapProjection projection,
        MapLayerSet layers,
        CancellationToken cancellationToken)
    {
        if (!layers.Players)
        {
            return ([], null);
        }

        var team = await query.GetTeamInfoAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        var ordered = (team?.Members ?? []).OrderBy(m => m.SteamId).ToList();
        if (ordered.Count == 0)
        {
            return ([], null);
        }

        var players = new List<PlayerPlacement>(ordered.Count);
        var legend = new List<MapLegendEntry>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var member = ordered[i];
            var color = PlayerPalette.For(i);
            var (px, py) = projection.ToPixel(member.X, member.Y);
            players.Add(new PlayerPlacement(member.Name, px, py, member.IsAlive, member.IsOnline, color.Rgba));
            legend.Add(new MapLegendEntry(color.Emoji, member.Name, StatusText(member)));
        }

        return (players, new MapLegend(legend));
    }

    private static string StatusText(Abstractions.Connections.TeamMemberSnapshot member)
    {
        var presence = member.IsOnline ? "online" : "offline";
        return member.IsAlive ? presence : presence + ", dead";
    }
```

Update the caller in `ComposeWithLayersAsync` (line 91-92) and the final `Render` return (lines 95-96):

```csharp
        var (players, legend) = await GatherPlayersAsync(guildId, serverId, projection, layers, cancellationToken)
            .ConfigureAwait(false);
        var rigPlacements = GatherRigs(guildId, serverId, serverMonuments, projection, layers);

        var png = renderer.Render(baseImage.Bytes, projection, markers, monuments, players, rigPlacements, layers,
            gridStyle, tunnels);
        return new MapComposition(png, legend);
```

- [ ] **Step 6: Update the host to unwrap the composition**

`ComposeAsync` now returns `MapComposition?`, so `MapHostedService.RefreshAsync` must unwrap it. Keep the **3-arg** poster call here (the legend overload lands in Task 7) so this task stays green. Change `RefreshAsync` (lines 194-200) to:

```csharp
        var composition = await _composer.ComposeAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (composition is null)
        {
            return;
        }

        await _poster.PostAsync(id, composition.Png, cancellationToken).ConfigureAwait(false);
```

(Task 7 Step 5 flips this to `_poster.PostAsync(id, composition.Png, composition.Legend, cancellationToken)`.)

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapComposerTests`
Expected: PASS (legend built per player, ordered by SteamId, status wording correct).

- [ ] **Step 8: Reformat + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add src/RustPlusBot.Features.Map/Composing/MapLegend.cs \
        src/RustPlusBot.Features.Map/Composing/MapComposer.cs \
        src/RustPlusBot.Features.Map/Hosting/MapHostedService.cs \
        tests/RustPlusBot.Features.Map.Tests/MapComposerTests.cs
git commit -m "feat(map): build a player legend alongside the rendered PNG"
```

---

### Task 7: Legend embed on the map image message

Attach the legend to the image message as a Discord embed.

**Files:**
- Create: `src/RustPlusBot.Features.Map/Posting/MapLegendEmbed.cs` (pure embed builder)
- Modify: `src/RustPlusBot.Features.Map/Posting/IMapChannelPoster.cs` (add `MapLegend?` param)
- Modify: `src/RustPlusBot.Features.Map/Posting/DiscordMapChannelPoster.cs` (send the embed)
- Modify: `src/RustPlusBot.Features.Map/Hosting/MapHostedService.cs:200` (pass the legend)
- Test: `tests/RustPlusBot.Features.Map.Tests/MapLegendEmbedTests.cs` (create)

**Interfaces:**
- Consumes: `MapLegend`, `MapComposition.Legend` (Task 6).
- Produces:
  - `static class MapLegendEmbed { static Embed? Build(MapLegend? legend); }` — null when legend is null/empty.
  - `IMapChannelPoster.PostAsync(ulong channelId, byte[] pngBytes, MapLegend? legend, CancellationToken cancellationToken)`.

- [ ] **Step 1: Write the failing embed-builder test**

Create `tests/RustPlusBot.Features.Map.Tests/MapLegendEmbedTests.cs`:

```csharp
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Posting;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapLegendEmbedTests
{
    [Fact]
    public void Build_returns_null_for_no_legend()
    {
        Assert.Null(MapLegendEmbed.Build(null));
        Assert.Null(MapLegendEmbed.Build(new MapLegend([])));
    }

    [Fact]
    public void Build_lists_one_line_per_player()
    {
        var legend = new MapLegend(
        [
            new MapLegendEntry("🟥", "Ada", "online"),
            new MapLegendEntry("🟦", "Bob", "offline, dead"),
        ]);

        var embed = MapLegendEmbed.Build(legend);

        Assert.NotNull(embed);
        Assert.Contains("Ada", embed!.Description, StringComparison.Ordinal);
        Assert.Contains("online", embed.Description, StringComparison.Ordinal);
        Assert.Contains("Bob", embed.Description, StringComparison.Ordinal);
        Assert.Contains("🟦", embed.Description, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapLegendEmbedTests`
Expected: FAIL — `MapLegendEmbed` does not exist.

- [ ] **Step 3: Implement the embed builder**

Create `src/RustPlusBot.Features.Map/Posting/MapLegendEmbed.cs`:

```csharp
using System.Globalization;
using System.Text;
using Discord;
using RustPlusBot.Features.Map.Composing;

namespace RustPlusBot.Features.Map.Posting;

/// <summary>Builds the Discord legend embed for a rendered map's players (color square, name, status).</summary>
public static class MapLegendEmbed
{
    /// <summary>Builds the legend embed, or null when there is nothing to show.</summary>
    /// <param name="legend">The legend, or null.</param>
    /// <returns>An embed, or null when the legend is null or empty.</returns>
    public static Embed? Build(MapLegend? legend)
    {
        if (legend is null || legend.Entries.Count == 0)
        {
            return null;
        }

        var description = new StringBuilder();
        foreach (var entry in legend.Entries)
        {
            description.Append(CultureInfo.InvariantCulture, $"{entry.Emoji} **{entry.Name}** — {entry.Status}")
                .Append('\n');
        }

        return new EmbedBuilder()
            .WithDescription(description.ToString().TrimEnd('\n'))
            .Build();
    }
}
```

- [ ] **Step 4: Add the legend to the poster contract + implementation**

In `IMapChannelPoster.cs`, change the signature:

```csharp
    Task PostAsync(ulong channelId, byte[] pngBytes, Composing.MapLegend? legend, CancellationToken cancellationToken);
```

Add `using RustPlusBot.Features.Map.Composing;` and drop the `Composing.` qualifier if preferred.

In `DiscordMapChannelPoster.cs`, update `PostAsync` (line 18) to accept the legend and pass the built embed to `SendFileAsync` (line 50-51):

```csharp
    public async Task PostAsync(ulong channelId, byte[] pngBytes, MapLegend? legend, CancellationToken cancellationToken)
```

and

```csharp
                await channel.SendFileAsync(stream, "map.png", embed: MapLegendEmbed.Build(legend),
                    options: options, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
```

Add `using RustPlusBot.Features.Map.Composing;` to the file.

- [ ] **Step 5: Pass the legend from the host**

In `MapHostedService.cs`, change the poster call (line 200) to the 4-arg overload:

```csharp
        await _poster.PostAsync(id, composition.Png, composition.Legend, cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 6: Run the whole map + workspace suites**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests tests/RustPlusBot.Features.Workspace.Tests`
Expected: PASS.

- [ ] **Step 7: Reformat + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add src/RustPlusBot.Features.Map/Posting/MapLegendEmbed.cs \
        src/RustPlusBot.Features.Map/Posting/IMapChannelPoster.cs \
        src/RustPlusBot.Features.Map/Posting/DiscordMapChannelPoster.cs \
        src/RustPlusBot.Features.Map/Hosting/MapHostedService.cs \
        tests/RustPlusBot.Features.Map.Tests/MapLegendEmbedTests.cs
git commit -m "feat(map): attach the player legend as an embed on the map image"
```

---

### Task 8: Full-solution verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build RustPlusBot.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test RustPlusBot.slnx`
Expected: all tests pass (no regressions across Map, Persistence, Workspace, Localization, Players, Events, Commands, Chat suites).

- [ ] **Step 3: Formatting gate**

Run: `dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx`
Expected: no diff (`git status` clean).

- [ ] **Step 4: Live smoke (during review)**

Run the bot against a live server and confirm on `#map`: the heli shows one rotor and the CH47 two (correctly placed); trails are faint dashes; the Tunnels toggle hides/shows tunnel entrances independently of Monuments; players render as distinct colored crosses (`+` alive / `x` dead, dimmed offline) with no name labels; and the image message carries a legend embed matching the cross colors. Grep the logs for `No monument icon for token` to confirm no tunnel-token icon regressions.

---

## Self-Review

**Spec coverage:**
- §A event icons/rotors → Task 1 (composer, 1 heli rotor / 2 CH47 rotors, vendor fix, patrol/ch47 removal). ✓
- §B subtle trails → Task 2. ✓
- §C tunnels layer (persistence, UI, routing, RustMaps SVG art reused) → Tasks 3, 4a, 4b. ✓
- §D players (colored `+`/`x` crosses, no labels, palette by SteamId, legend embed) → Tasks 5, 6, 7. ✓
- Plumbing (PlayerPlacement color, MapComposition, poster signature, host wiring) → Tasks 5–7. ✓
- Testing (composer/renderer/palette/legend/tunnels/persistence/localization) → per-task tests + Task 8. ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code; churn edits enumerate exact lines. ✓

**Type consistency:** `MapComposition.Png`/`.Legend`, `MapLegendEntry(Emoji,Name,Status)`, `PlayerPalette.For(int).Rgba/.Emoji`, `PlayerPlacement(...,Color CrossColor)`, `MapLayerSet(...,bool Tunnels=false)`, `MapLayerSettings(...,bool Tunnels=true,MapGridStyle GridStyle=…)`, `IMapChannelPoster.PostAsync(ulong,byte[],MapLegend?,CancellationToken)`, `MapRenderer.Render(...,IReadOnlyList<MonumentPlacement>? tunnels=null)` — used consistently across tasks. ✓

**Known cross-task ordering:** Task 6 keeps the 3-arg poster call (green); Task 7 flips it to 4-arg. The `MapLayerSettings.Tunnels`-defaults-true change (Task 3) forces the `ComposeAsync_renders_only_enabled_layers` fix in Task 4a Step 7 and the control-message test in Task 4b — both are called out. ✓
