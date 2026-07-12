# Map Render Rework — Design

**Date:** 2026-07-06
**Branch:** `feat/map-render-rework` off `develop` (plain branch, no worktree)
**Status:** Approved by user (brainstorm 2026-07-06)

## Problem

The #map channel render is unusable (see `tmp/images/map.png` vs `tmp/images/official-app.jpg`).
Four root causes, all confirmed by code inspection and cross-checked against
rustplusplus (`src/structures/Map.js`) and rustplus-desktop (`MainWindow.Map.*.cs`):

1. **Wrong coordinate transform.** `Rendering/WorldToPixel.cs` treats the base JPEG's
   *pixel* `Width`/`Height` as world units and adds `OceanMargin` (pixels) to world
   coordinates. The real world size (`ServerInfo.MapSize`) is never consumed anywhere in
   the render path. Every grid line, monument, and marker is misplaced.
2. **Icons drawn at native asset resolution.** `MapRenderer.CenterAt` uses
   `icon.Width/Height` unscaled; `oilrig.png` is 875×875 on a 1024×1024 canvas. No icon
   scaling exists anywhere.
3. **Moving markers frozen.** `ConnectionSupervisor.PublishMarkerDeltaAsync` publishes
   only on Added/Removed *by id*. Marker ids are stable while cargo/heli/chinook move, so
   `EventStateStore` never updates their X/Y; the periodic repaint redraws first-seen
   positions forever.
4. **Rotation lost at the library boundary.** The protobuf `AppMarker.rotation` (field 6)
   exists but `RustPlusApi.Data.Markers.Marker` doesn't map it. Icons can't follow
   heading.

Latent bonus bug: `Events/Formatting/GridReference.cs` clamps world coords against the
image *pixel* width — #events embeds report wrong grid cells for world positions beyond
~image-width units. Also `146.25` is duplicated in `MapRenderer` and `GridReference`,
and the dead/offline player color styling in `DrawPlayerIcon` is unreachable.

## Decisions (from brainstorm)

- **Visual target:** rustplus-desktop style — scaled monument *icons* (not text names),
  per-cell grid labels, rotated event icons.
- **Base map:** RustMaps preferred, Rust+ JPEG fallback. The map is constructed from
  multiple data sources: RustMaps static imagery + Rust+ live data.
- **Rotation:** wait for RustPlusApi `2.0.0-beta.4`. Requirements documented in
  `RustPlusApi/docs/development/beta4-map-marker-rotation.md` (written 2026-07-06).
  Bot-side plumbing ships now with `Rotation` nullable; renders unrotated until the lib
  lands.
- **Motion:** 30s refresh + fading trails.
- **Architecture:** Approach A — extend the existing delta path with a `Moved` bucket;
  map keeps rendering from `EventStateStore`. No compose-time refetch, no duplicate
  polling.

## Architecture

### 1. Projection & grid

**`MapProjection`** (new, `Features.Map/Rendering/`, replaces `WorldToPixel`): immutable
value type constructed per render from `(worldSize, baseImagePixelWidth,
baseImagePixelHeight, oceanMarginPx, outputSize)`. Canonical transform (matches
rustplusplus `Map.js:344-346,389-391`):

```text
imgX = worldX * ((imgW - 2*margin) / worldSize) + margin
imgY = imgH - (worldY * ((imgH - 2*margin) / worldSize) + margin)
outputPx = imgPx * (outputSize / imgDimension)     // per axis
```

World origin bottom-left (Y up), image origin top-left (Y down).

**WorldSize plumbing:** `MapDimensions` record gains `uint WorldSize`, populated in the
connection shim (`RustPlusSocketSource`) from `GetInfoAsync().MapSize`, cached per
connection (static per wipe, same lifecycle as the base map cache).

**`MapGrid`** (new, single source of grid math): cell = `146.25f` world units (constant
lives here only), spreadsheet columns A…Z, AA…, rows numbered from the **top** (row 0 =
north). Used by:

- `MapRenderer` — thin semi-transparent lines over the world square + small per-cell
  labels at each cell's top-left (LiberationSans, existing embedded font).
- `Events/Formatting/GridReference` — refactored to delegate to `MapGrid` with real
  `WorldSize`. Fixes the latent #events grid-ref bug. `GridReference`'s public surface
  stays (callers untouched).

`MapGrid` placement: **`Abstractions`** (zero-dependency pure math). `Features.Map`
already references `Features.Events`, so Events can never reference Map — Abstractions
is the only home both can share without a cycle.

### 2. Base-map source chain

**`IBaseMapSource`** (new): `Task<BaseMapImage?> GetAsync(guild, server, ct)` returning
`BaseMapImage(byte[] Bytes, int PixelWidth, int PixelHeight, int OceanMarginPx)`.

1. **`RustMapsBaseMapSource`** — active only when `Map:RustMaps:ApiKey` is configured.
   Resolves the server's procedural map via `IRustMapsClient.GetMapBySeedAndSizeAsync(
   (int)info.MapSize, (int)info.Seed, staging: false)`, downloads **`RawImageUrl`**
   (clean terrain, no baked monument icons — we draw our own). OceanMargin: expected 0
   for RustMaps raw renders — **verify against a real fetch during implementation** and
   encode whatever the actual format is. Any failure (no key, 404 map not generated,
   HTTP error) → return null → fall through.
2. **`RustPlusBaseMapSource`** — today's `GetMapImageAsync` JPEG + `GetMapDimensionsAsync`
   pixel dims/margin.

`BaseMapCache` stores `BaseMapImage` (not bare bytes) per (guild, server); cleared on
disconnect exactly as today. `MapComposer` builds `MapProjection` from the cached
`BaseMapImage` + `WorldSize`.

RustMaps' `ImageUrl` (icons baked in) is NOT used at runtime — reserved for the
verification harness.

**Package:** `RustMapsApi 1.0.0-beta.1` (NuGet) added to `Directory.Packages.props`;
DI via its `ServiceCollectionExtensions` with the key from options. Only
`Features.Map` references it.

### 3. Rendering

**`MapRenderStyle`** (new constants class): all sizing in one place, as fractions of
`OutputSize` (1024):

- monument icons ≈ 3% (~30px), event icons ≈ 3.5% (cargo ~40px), player ≈ 2% (~20px)
- trail width, trail alpha ramp, grid line alpha, label font size
- exact values tuned during implementation against `official-app.jpg` / desktop refs

**Icon scaling:** `MapIcons` keeps serving native `Image<Rgba32>`; the renderer obtains
scaled copies through a per-(key,size) cache (immutable images, clone-resize once).
Rig active-ring radius derives from the scaled icon.

**Rotation:** `MapMarkerSnapshot` gains `float? Rotation` (nullable end-to-end). When
non-null, moving markers (cargo/heli/chinook/vendor) draw rotated by `−rotation` about
their center (desktop-app convention for cargo/heli/chinook on a Y-down canvas); the
desktop app applies per-type flip corrections (e.g. +180° for some kinds), so per-kind
corrections are settled during the visual verification pass, encoded as constants in
`MapRenderStyle`. Null → unrotated. Until RustPlusApi beta.4 ships, the shim maps
`Rotation: null`; wiring the real value is a one-line change later.

**Trails:** for each active marker with ≥2 history points, draw a polyline through the
ring positions beneath the icons — oldest faintest (alpha ramp), colored per marker kind.

**Players:** scaled `player.png`; offline/dead members render at reduced opacity
(grayscale acceptable if simpler in ImageSharp) — replaces the unreachable colored-dot
branch. Existing label suffixes stay.

**Draw order:** base → grid → monuments → trails → markers → rigs → players (each still
layer-gated by `MapLayerSet`).

### 4. Movement through the delta path

- `ConnectionSupervisor.PublishMarkerDeltaAsync`: compute `Moved` = ids present in both
  polls whose X/Y (or Rotation, later) changed; publish `MapMarkersChangedEvent` when
  Added ∪ Removed ∪ Moved ≠ ∅. Event record gains `IReadOnlyList<MapMarkerSnapshot>
  Moved` (in-process event, additive — same pattern as PR #44's
  `ConnectionStatusChangedEvent.IsConnected`).
- `EventStateStore.Apply`: `Moved` → update the `ActiveMarker`'s X/Y/Rotation and append
  to its position history ring (cap 6, oldest evicted); `Added` seeds the ring with the
  first position. `ActiveMarker` gains the ring + `Rotation`.
- `MarkerEventClassifier` and `EventRelay` ignore `Moved` — no new #events messages.
- Refresh cadence: `MapHostedService` unchanged structurally; the more frequent event
  publishes are absorbed by `MapRefreshThrottle` at the new 30s interval.

### 5. Configuration

- `MapOptions.MapRefreshInterval` default 45s → **30s**.
- New nested options class `RustMapsOptions { string? ApiKey }` exposed as
  `MapOptions.RustMaps`, bound from `Map:RustMaps:ApiKey`. Null/empty → RustMaps source
  inactive; no other behavior change. ValidateOnStart stays happy with the key absent
  (it's optional).

### 6. Error handling

- RustMaps failures are silent-with-log fallbacks to the Rust+ JPEG (the map must render
  on custom/unpublished maps and when RustMaps is down).
- Base map missing entirely → no post (today's behavior).
- `WorldSize` unavailable (info call failed) → render base tile only, overlays off
  (mirrors today's null-dims guard); never render overlays with a guessed size.
- Renderer never throws on a missing icon (existing skip-unknown behavior preserved).

## Testing

TDD per repo convention (xunit, sequential verification runs):

- **`MapProjectionTests`** — golden numbers: known (worldSize, imgDims, margin) triples
  from both source styles (Rust+ margin>0; RustMaps margin=0), corners/center/Y-flip.
- **`MapGridTests`** — cell counts, labels, and boundaries for real map sizes
  (3000/3500/4250/4500); regression: world coords beyond image-pixel-width map to
  correct cells (the old GridReference bug).
- **`GridReferenceTests`** — updated to feed WorldSize; existing expectations hold.
- **Delta tests** — `Moved` detected on position change; not published when nothing
  changed; Added/Removed semantics untouched.
- **`EventStateStoreTests`** — ring seeding, cap-6 eviction, position/rotation update.
- **`MapRendererTests`** — scaled icon stays within expected bounds (no 875px blobs:
  assert changed-pixel bounding box ≤ style size + tolerance), trail pixels present for
  a marker with history, rotation smoke (non-null rotation changes output bytes).
- **`RustMapsBaseMapSourceTests`** — fake HTTP handler: happy path, 404 → null,
  no-key → inactive.
- **Parity fixture test** — committed `MapInfo` JSON (real RustMaps response for a
  known size+seed) + matching Rust+ monument tokens: both projected through
  `MapProjection`, assert same cells / within pixel tolerance.

**Manual verification harness** (pre-merge gate, `tools/` script or throwaway sample):
fetch the live RustMaps render (`ImageUrl`, icons baked) for the user's server
(size+seed from `ServerInfo`), overlay our projected monument positions, save a
side-by-side/diff PNG for eyeball comparison. Definitive math check against ground
truth. Then a live bot run compared against `tmp/images/official-app.jpg`.

## Dependencies & gates

- **RustPlusApi `2.0.0-beta.4`** (user publishes): `Marker.Rotation` + `ServerMap` doc
  fixes, per `RustPlusApi/docs/development/beta4-map-marker-rotation.md`. Only the
  rotation *values* gate on this; everything else ships against beta.3.
- **RustMapsApi `1.0.0-beta.1`** (NuGet, published).
- ImageSharp stays 3.1.12 / Drawing 2.1.7 (Apache — never bump Drawing to 3.x).

## Out of scope (future slices)

- Vending-machine markers layer (lib DTOs exist; new toggle + icons + sell-order data).
- Player Steam avatars (deferred again from 2b-ii).
- Zoom/crop regional views; animated GIF output.
- RustMaps `ImageUrl` as a "monuments baked in" base-map option.
- Dead-reckoning for vanished markers (desktop app does this; our markers despawn).
