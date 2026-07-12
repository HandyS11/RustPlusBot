# RustMaps monument icons via RustMapsApi.Assets — Design

**Date**: 2026-07-11
**Status**: Approved (design); spec pending user review
**Branch (planned)**: `feat/rustmaps-monument-icons` off `develop`

## Problem

The map render uses 35 vendored monument PNGs (embedded in `RustPlusBot.Features.Map/Assets/icons/`). They are dated, stylistically inconsistent with the RustMaps look the bot already uses for the `#info` static map, and several monuments Rust+ broadcasts have no icon at all (radtown, jungle ziggurat, underwater labs, tunnel entrances, train tunnel links). Unknown monument tokens are silently skipped, so gaps are invisible.

## Goal

1. Replace all **monument** icon art with `RustMapsApi.Assets 1.0.0-beta.3` (SVG, RustMaps red-badge style), keeping the non-monument icons (cargo, patrol heli, CH-47, travelling vendor, player) as vendored PNGs.
2. Full coverage: map every monument token Rust+ is known to broadcast to a package asset.
3. Log **Information** when a monument has no icon (unmapped token, or mapped type without package art) so gaps can be chased later — deduped to once per unique token per process.

## Decisions made during brainstorming

- **Coverage**: full — every known Rust+ token, not just the current 35. (User choice.)
- **Rasterization**: at **runtime** via Svg.Skia, not a build-time generator tool. (User choice.) The package is consumed directly by the bot; art updates arrive with package version bumps; the 35 monument PNGs are deleted from the repo.
- **Rigs included**: the oil-rig strip icons (`MapIcons.Rig`) draw the same monument art (`OilrigSmall`/`OilrigLarge`), so they move to the new source too.
- **Logging cadence**: Information level, once per unique token per process lifetime, message contains the raw token.

## Package facts (verified against the published nupkg)

- `RustMapsApi.Assets 1.0.0-beta.3`: 42 embedded SVGs keyed by `RustMapsApi.V4.Models.MonumentType`. API: static `MonumentAssets.TryGetAsset/GetAsset/HasAsset/AvailableTypes` plus DI `IMonumentAssetSource` via `services.AddRustMapsAssets()`. Targets net10.0 / netstandard2.0.
- SVGs are 40×40 viewBox, use CSS-classed `<style>` blocks → require a real SVG engine (Svg.Skia); no `<text>` elements → no fontconfig dependency needed.
- Depends on `RustMapsApi >= 1.0.0-beta.3`; the repo currently pins `1.0.0-beta.2` in `Directory.Packages.props` → **bump required**. `RustMapsGenerationDriver` (only current consumer) must be re-verified against beta.3.

## Architecture

### Packages (`Directory.Packages.props` + `RustPlusBot.Features.Map.csproj`)

- Add `RustMapsApi.Assets` `1.0.0-beta.3`.
- Bump `RustMapsApi` `1.0.0-beta.2` → `1.0.0-beta.3`.
- Add `Svg.Skia` (+ transitively `SkiaSharp`) and `SkiaSharp.NativeAssets.Linux.NoDependencies` (dev host Fedora + CI Linux; `NoDependencies` variant avoids fontconfig/glibc surprises).

### `MonumentTokenMap` (new; replaces `MonumentIconMap`)

Static, pure: `MonumentType? TypeFor(string token)`.

- Exact-match dictionary (table below).
- Two prefix rules, applied after the exact lookup misses: tokens starting with `swamp` → `SwampC`; tokens starting with `underwater_lab` → `UnderwaterA`. Rust+ sends prefab names, not fixed tokens, for these (confirmed in olijeffers0n/rustplus, which special-cases both).
- Unknown → `null` (caller logs).

Token → `MonumentType` table (sources: current `MonumentIconMap`, alexemanuelol/rustplusplus `src/structures/Map.js`, olijeffers0n/rustplus `rustplus/utils/utils.py`). Where several enum values share one asset (e.g. `FishingVillageA/B/C` → `Fishing_Village`), any value with the right art is acceptable; the pick below is arbitrary but fixed.

| Rust+ token | MonumentType | Asset |
|---|---|---|
| `AbandonedMilitaryBase` | `MilitaryBaseA` | Military_Base |
| `airfield_display_name` | `Airfield` | Airfield |
| `arctic_base_a` | `ArcticResearchBaseA` | Arctic_Research_Base |
| `arctic_base_b` | `ArcticResearchBaseA` | Arctic_Research_Base |
| `bandit_camp` | `BanditTown` | Bandit_Town |
| `dome_monument_name` | `SphereTank` | Sphere_Tank |
| `excavator` | `Excavator` | Excavator |
| `ferryterminal` | `FerryTerminal1` | Ferry_Terminal_1 |
| `fishing_village_display_name` | `FishingVillageA` | Fishing_Village |
| `large_fishing_village_display_name` | `FishingVillageB` | Fishing_Village |
| `gas_station` | `Gasstation` | Gasstation |
| `harbor_display_name` | `HarborLarge` | Harbor |
| `harbor_2_display_name` | `HarborSmall` | Harbor |
| `jungle_ziggurat` | `JungleZigguratA` | Ziggurat |
| `junkyard_display_name` | `Junkyard` | Junkyard |
| `large_oil_rig` | `OilrigLarge` | Oilrig_Large |
| `oil_rig_small` | `OilrigSmall` | Oilrig_Small |
| `launchsite` | `LaunchSite` | Launch_Site |
| `lighthouse_display_name` | `Lighthouse` | Lighthouse |
| `military_tunnels_display_name` | `MilitaryTunnels` | Military_Tunnels |
| `mining_outpost_display_name` | `Warehouse` | Warehouse |
| `mining_quarry_hqm_display_name` | `HqmQuarry` | Hqm_Quarry |
| `mining_quarry_stone_display_name` | `StoneQuarry` | Stone_Quarry |
| `mining_quarry_sulfur_display_name` | `SulfurQuarry` | Sulfur_Quarry |
| `missile_silo_monument` | `NuclearMissileSilo` | Nuclear_Missile_Silo |
| `outpost` | `Outpost` | Outpost |
| `power_plant_display_name` | `Powerplant` | Powerplant |
| `radtown` | `Radtown` | Radtown |
| `satellite_dish_display_name` | `SatelliteDish` | Satellite_Dish |
| `sewer_display_name` | `SewerBranch` | Sewer_Branch |
| `stables_a` | `StablesA` | Large_Barn |
| `stables_b` | `StablesB` | Large_Barn |
| `supermarket` | `Supermarket` | Supermarket |
| `swamp*` (prefix) | `SwampC` | Swamp |
| `train_tunnel_display_name` | `TunnelEntrance` | Tunnel_Entrance |
| `train_tunnel_link_display_name` | `TunnelEntranceTransition` | Tunnel_Entrance_Transition |
| `train_yard_display_name` | `Trainyard` | Trainyard |
| `underwater_lab*` (prefix) | `UnderwaterA` | Underwater_Lab |
| `water_treatment_plant_display_name` | `WaterTreatment` | Water_Treatment |

Package assets with no known broadcast token today (caves, icebergs, powerlines, power substations, water wells, jungle ruins, apartments complex): intentionally unmapped. If Rust+ starts broadcasting them, the Info log surfaces the token and the map gains one dictionary line.

### `MonumentIconSource` (new; DI singleton)

The only new runtime service. Responsibilities:

- `Image<Rgba32>? Monument(string token, int size)` — `MonumentTokenMap.TypeFor` → `IMonumentAssetSource.TryGetAsset` → rasterize → cache.
- `Image<Rgba32>? Rig(RigKind kind, int size)` — same path via `OilrigSmall`/`OilrigLarge`.
- Rasterization: `asset.OpenStream()` → `Svg.Skia.SKSvg` → render scaled to `size`×`size` (square viewBox scales cleanly) → PNG-encode → `Image.Load<Rgba32>`. One-time cost per (type, size); cached in a `ConcurrentDictionary<(MonumentType, int), Image<Rgba32>?>`. Cached images are immutable inputs (same contract as `MapIcons`).
- Logging: `ILogger<MonumentIconSource>`, Information, **once per unique token per process** (a `ConcurrentDictionary<string, byte>` of already-logged tokens), covering both failure shapes: token unmapped, and mapped type without asset. Message carries the raw token, e.g. `Monument token "{Token}" has no icon; skipped.` Rasterization failures log Warning (also once) and return null.
- Constructor: `(IMonumentAssetSource assets, ILogger<MonumentIconSource> logger)`.

### Diet for existing types

- `MapIcons`: keeps `Marker`, `Player`, `Vendor` (PNGs `cargo`, `ch47`, `patrol`, `player`, `vendor`); `Monument` and `Rig` members are deleted.
- `MonumentIconMap`: deleted.
- Embedded PNGs: the 35 monument files deleted (including the never-mapped `powerstation.png`, `waterwell.png`); the 5 non-monument PNGs and the font stay. `EmbeddedResource` glob in the csproj is unchanged (`icons/*.png` still matches the keepers).
- `MapRenderer`: gains a constructor dependency on `MonumentIconSource` (it is already registered as a DI singleton); `DrawMonuments` and `DrawRigs` become instance methods calling the source. Draw sizes unchanged (`MapRenderStyle.MonumentIconSize = 30`).
- DI: Features.Map service registration adds `services.AddRustMapsAssets()` and the `MonumentIconSource` singleton.

## Data flow

```
Rust+ AppMap monument token
  → MonumentTokenMap.TypeFor (exact, then prefix rules)
  → IMonumentAssetSource.TryGetAsset(MonumentType)
  → Svg.Skia raster @ requested px  →  Image<Rgba32> cache
  → MapRenderer.DrawMonuments / DrawRigs
  (miss anywhere → skip icon + one-time Info log with the token)
```

## Error handling

- Unmapped token → skip, Info (once per token/process).
- Mapped type, no asset → skip, Info (once).
- SVG rasterization throws → skip, Warning (once), null cached so it isn't retried per render.
- Package/DI misconfiguration (no `IMonumentAssetSource`) → startup DI failure, loud by design.

## Testing

- `MonumentTokenMapTests`: table-driven exact matches; prefix rules (`swamp_a`, `underwater_lab_d` and full prefab-style names); unknown → null; null/empty token safe.
- **Drift guard**: for every mapped `MonumentType`, `MonumentAssets.HasAsset` is true — fails loud if a package update drops art (mirrors the choice⇆dataset drift guard from 6e).
- `MonumentIconSourceTests`: returns image with max edge == requested size; same call returns the cached instance; unknown token → null + exactly one Info log across repeated calls (FakeLogger); rig kinds resolve; rasterization of every mapped type succeeds (doubles as an SVG-engine smoke test).
- `MapRendererTests`: updated for constructor injection; monument/rig layers still render (non-null pixels where icons land).
- `MapIconsTests`: monument/rig members removed; keeper icons still load.
- Verify `tools/RustPlusBot.MapParity` still builds/runs (it exercises the render path; blend-verification vs RustMaps should if anything improve, but parity thresholds may need re-baselining since monument art changes).

## Risks / notes

- `RustMapsApi` beta.2 → beta.3 bump: user's own package; re-verify `RustMapsGenerationDriver` compiles and `#info` map generation still works.
- SkiaSharp native binaries (~9 MB) join the deployment; `NoDependencies` Linux variant chosen deliberately. CI (Linux) and Fedora dev host both covered. If the bot is ever run on Windows/mac for dev, SkiaSharp's default package already bundles those natives.
- New-art look: monument icons become RustMaps red-badge style at 30 px — intentional, matches the `#info` RustMaps map.
- Icon licensing: RustMaps artwork stays inside the NuGet package (their attribution terms); repo stops carrying monument art entirely.
- `dotnet jb cleanupcode` gate applies to all new files.
