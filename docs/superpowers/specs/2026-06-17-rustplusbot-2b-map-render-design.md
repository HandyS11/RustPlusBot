# Subsystem 2b — Rendered live map in `#map` (design)

**Date:** 2026-06-17
**Status:** Design approved (brainstorming), pending spec review → plan
**Branch:** `feat/map-render` off `develop` (plain branch — NO worktree)
**Predecessors:** 2a (live events, PR #13 merged) and 2a-ii (oil-rig events, PR #14 merged).

## 1. Goal & surface

Subsystem 2 is "map + live events". 2a/2a-ii shipped the live event layer (`#events`
alerts, `!small`/`!large`, oil-rig state machine). 2b ships the **rendered map image**.

The map is surfaced as a **new per-server `#map` Discord channel** — a fourth per-server
channel alongside `#info` / `#teamchat` / `#events`, using the `ReadOnly` permission profile
(one-way bot feed, like `#events`). The channel holds **one bot-managed message** whose
attachment is a rendered PNG of the server map with overlays drawn on it.

There is **no `/map` slash command** (an earlier idea, dropped during brainstorming): the user
wants the map as a channel image, not an on-demand command.

The image is refreshed by **delete + repost** of that single message (the only reliable way to
swap a Discord attachment — `ModifyAsync` cannot replace an attachment), **throttled / coalesced
to at most once per `MapRefreshInterval` (~45 s) per server**, driven by the marker/connection
events 2a already publishes.

## 2. Scope split (2b core vs 2b-ii)

This mirrors the 2a → 2a-ii split: de-risk the genuinely-new image-rendering technology first,
defer breadth/config to a follow-up.

### 2b (THIS slice)

- New `RustPlusBot.Features.Map` project.
- ImageSharp rendering pipeline: `WorldToPixel` + `MapRenderer` → ~1024 px PNG.
- New per-server `#map` `ChannelSpec` (`ReadOnly`) + EN/FR `channel.map.name`.
- Connections shim extension: `GetMapImageAsync` (the base `JpgImage` bytes), cached once per
  connection (the base map is static per wipe).
- Layers drawn, **fixed ON** (no config yet): **base map + grid + live markers
  (cargo ship / patrol helicopter / chinook)**.
- `MapHostedService` (throttled, event-driven refresh; clears on disconnect),
  `DiscordMapChannelPoster` (delete + repost), `IMapChannelLocator`, `MapOptions`.
- **No entity, no migration.**

### 2b-ii (recorded here so it is unblocked; NOT built in this slice)

- `ServerMapSettings` entity (per `(guild, server)`, FK → `RustServer` cascade, bool toggles
  `ShowGrid`/`ShowMonuments`/`ShowMarkers`/`ShowVendor`/`ShowRigs`, defaults all ON) + migration
  `MapSettings` + `IMapSettingsStore`.
- ManageGuild-gated layer-toggle UI (buttons) on the `#map` message.
- **Monuments layer** — per-monument icons keyed by the monument's protobuf token; **unknown /
  unmapped tokens are skipped** (not drawn).
- **Wandering trader / travelling vendor** — new `MarkerKind.TravellingVendor` + extend the marker
  shim's bucket loop to surface `MapMarkers.TravellingVendorMarkers` (2a reads only
  cargo/heli/chinook).
- **Oil-rig activation styling** — reuse 2a-ii's `IRigState` to style the small/large rig icon
  Active (combat) vs Online.

## 3. Source data (RustPlusApi 2.0.0-beta.1, verified)

`RustPlus.GetMapAsync()` → `Response<RustPlusApi.Data.ServerMap>`:

- `JpgImage` — "Raw JPEG image bytes of the map tile, if available." **This is the base map.**
  2a/2a-ii read only `Width`/`Height`/`OceanMargin`/`Monuments` from `ServerMap`, never the image
  bytes — 2b adds reading `JpgImage`.
- `Width` / `Height` (game units), `OceanMargin` — already mapped into `MapDimensions`.
- `Monuments` — `List<ServerMapMonument>` (`Name` = protobuf token e.g. `oilrig_1`, `X`/`Y`); used
  in 2b-ii's monuments layer (2a-ii already reads these via `GetMonumentsAsync`).

Live marker positions come from 2a's in-memory `IEventState` (the `ConnectionSupervisor`
marker-poll task already tracks active cargo/heli/chinook markers stamped with their
`MapDimensions`). 2b does **not** add a new socket round-trip for markers — it reads the cache that
already exists, the same source `#events` is fed from.

The `MapMarkersChangedEvent` (Abstractions, ns `RustPlusBot.Abstractions.Events`) already carries
`GuildId`, `Guid ServerId`, `MapDimensions`, and added/removed marker lists — it is the refresh
trigger.

## 4. Architecture — `RustPlusBot.Features.Map`

New project, mirroring `RustPlusBot.Features.Events`. References: Abstractions, Persistence,
Discord, Domain, Workspace, Connections. Registered via `AddMap(...)` in the Host's `Program.cs`.

| Unit | Kind | Responsibility | Tested |
|------|------|----------------|--------|
| `Rendering/WorldToPixel` | pure static | `(worldX, worldY, MapDimensions, int outputSize) → (px, py)`. Encapsulates the OceanMargin offset + game-units→pixels scale + Y-flip (game Y is south→north, image Y is top-down). | yes |
| `Rendering/MapLayerSet` | value object | Which layers to draw (`Grid`, `Markers`, `Monuments`, `Vendor`, `Rigs` bools). 2b passes a constant `MapLayerSet.Default2b` (grid + markers on); 2b-ii feeds it from the store. **Defined now so 2b-ii needs no `MapRenderer` signature change.** | n/a |
| `Rendering/MapRenderer` | pure-ish | Input: base JPEG bytes + `MapDimensions` + overlay item list + `MapLayerSet`. Decodes base JPEG (ImageSharp), downscales to ~1024 px max edge, draws grid lines+labels, composites marker icons, encodes PNG → `byte[]`. No I/O beyond the asset registry. | yes (non-empty PNG, expected size, deterministic given inputs) |
| `Assets/MapAssets` | keyed registry | Loads embedded marker icon PNGs by key (`cargo`, `heli`, `chinook` in 2b; `+monuments/vendor` in 2b-ii). Keyed lookup so icons swap without code change. | yes (each key resolves to a decodable image) |
| `Composing/MapComposer` | logic | Gathers cached base map + `IEventState` active markers + `MapLayerSet` → builds the `MapRenderer` input. Returns null when no base map is available yet. | yes |
| `Posting/IMapChannelPoster` + `DiscordMapChannelPoster` | Discord shim | Delete the bot's prior message in the channel (if any), repost the PNG as an attachment. **Untested integration shim** (like `DiscordEventChannelPoster`). | no |
| `Hosting/MapHostedService` | background | Subscribes `MapMarkersChangedEvent` + `ConnectionStatusChangedEvent`; per-`(guild,server)` throttle gate; resolves `#map` channel via `IMapChannelLocator`; on disconnect clears the cached base map + removes the message. | gate logic tested |
| `Rendering/MapLocalizationCatalog` + localizer | strings | EN/FR strings (channel name handled by Workspace; this is for any in-image/embed text). Copy of the Events localizer shape. | yes |
| `MapServiceCollectionExtensions` | DI | `AddMap()` wires the above. | via registration test |

### Base-map cache

The base map (`JpgImage` bytes + `MapDimensions`) is fetched **once per connection** and cached
in-memory (static per wipe). 2b adds:

- `IRustServerConnection.GetMapImageAsync(timeout, ct)` → `byte[]?` (reads
  `GetMapAsync().Data.JpgImage`; null on failure/missing). Implemented on the untested
  `RustPlusSocketSource` shim + the `RejectedConnection` no-op + `FakeRustSocketSource`.
- A dedicated `Composing/BaseMapCache` **singleton** holding a per-`(guild,server)` cache of
  `(byte[] image, MapDimensions dims)`: on first render after connect it fetches via the query seam
  + caches; `Clear(guild, server)` on disconnect. A singleton (not state inside `MapComposer`,
  which is resolved per use) is required so the cache survives across refreshes. `MapComposer`
  reads from it.
- The fetch goes through the existing `IRustServerQuery` seam (add `GetMapImageAsync(guild, server,
  ct)` → `byte[]?`, backed by the supervisor's live socket over the internal
  `IRustServerConnection.GetMapImageAsync`), keeping RustPlusApi types out of `Features.Map`.

### Refresh / throttle flow

`MapHostedService` keeps a per-`(guild,server)` "dirty + last-rendered-at" gate. On any relevant
event it marks dirty; the actual render+repost runs at most once per `MapRefreshInterval`
(coalescing bursts of marker ticks). On `ConnectionStatusChangedEvent` where status is not
`Connected`, it clears the base-map cache and removes the `#map` message.

`MapOptions.MapRefreshInterval` (default `TimeSpan.FromSeconds(45)`, validated `> Zero` via
`AddOptions<MapOptions>().Validate().ValidateOnStart()` in `Program.cs`, same pattern as
`ConnectionOptions`).

## 5. Workspace integration

- Add the `#map` `ChannelSpec` to `ServerWorkspaceSpecProvider`:
  `new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerMap, "channel.map.name",
  ChannelPermissionProfile.ReadOnly, 3)` and a `ServerMap = "map"` key in `WorkspaceChannelKeys`.
  The reconciler provisions/heals it like the other per-server channels — no Workspace logic
  change beyond the new spec row + localized name.
- EN/FR `channel.map.name` (e.g. `map` / `carte`).
- New `IMapChannelLocator` + `MapChannelLocator` in `Features.Map` (or Workspace, matching
  `EventChannelLocator`): a 30 s-TTL cache mapping `(guild, server) → channelId`, a copy of
  `EventChannelLocator` (game→Discord direction only). It reads the existing
  `IWorkspaceStore.GetChannelsByKeyAsync`.

The `#map` message is NOT a Workspace `MessageSpec` (those are in-place-edited single messages);
the map message is managed by `DiscordMapChannelPoster`'s delete+repost, outside the reconciler's
message model — exactly as `#events` posts are outside it.

## 6. Output format

- Render at a fixed **max edge ~1024 px** (downscale the base tile if larger), encode **PNG**.
  Well under Discord's 8 MB unboosted attachment cap, fast to encode each refresh, crisp overlays
  (no JPEG artifacts on grid lines / icons), readable inline.
- ImageSharp packages added to `Directory.Packages.props`: `SixLabors.ImageSharp` and
  `SixLabors.ImageSharp.Drawing` (centrally versioned). Pure-managed, no native dependency —
  important for the self-hosted Linux host.

## 7. Icon assets & licensing

Marker icons (and 2b-ii's monument icons) are sourced from the rustplusplus
(`src/resources/images/markers`) and rustplus-desktop (`Assets/icons`) repositories. Both repos
are GPL-3.0; **this repository is MIT.**

**Decision (explicit):** these icons are **Facepunch / Rust game assets that the GPL repos merely
redistribute** — they are not original GPL-licensed work of those projects, so the repos' GPL does
not attach to the third-party game art. They are bundled here as embedded resources and used under
the same companion-app fair-use norm as the official Rust+ app and both reference bots. They are
**not** treated as GPL and do **not** encumber this MIT repo. A `NOTICE`/attribution line records
their origin as Facepunch/Rust game art. The `MapAssets` registry is keyed so any icon can be
swapped (or replaced with an original/relicensed set) without code changes.

For 2b specifically: only the three marker icons (`cargo`, `heli`, `chinook`) are vendored. If even
those are undesirable, `MapRenderer` can fall back to drawn glyphs (colored circle + letter) — the
keyed registry makes this a localized change, not an architectural one.

## 8. Testing & untested shims

- **Pure / logic units fully tested:** `WorldToPixel` (coordinate math incl. ocean-margin offset
  and Y-flip), `MapRenderer` (deterministic PNG from a tiny fixed base image + fixed overlays;
  assert magic bytes + dimensions), `MapAssets` (keys resolve), `MapComposer` (layer selection +
  null when no base map), the `MapHostedService` throttle gate, `MapChannelLocator` (TTL behaviour
  via `IClock`).
- **Untested integration shims** (repo convention): `DiscordMapChannelPoster` and the new
  `GetMapImageAsync` on `RustPlusSocketSource`.
- **`FakeRustSocketSource`** must implement the new `GetMapImageAsync` member (or the Connections
  test assembly silently drops tests — the recurring "fake must implement new interface members"
  lesson). Run the FULL suite and read per-assembly counts.
- No interaction-module unit tests (none in this repo); 2b has no interaction module anyway (the
  toggle UI is 2b-ii).

## 9. Forward-coupling guards (so 2b-ii needs no rework)

1. `MapRenderer` takes an explicit `MapLayerSet` value object from day one (2b passes a constant;
   2b-ii feeds it from `IMapSettingsStore`).
2. `MapAssets` is a keyed registry (2b: 3 icons; 2b-ii: + monuments/vendor — just add files +
   keys).
3. Neither the marker shim nor `MapComposer` assumes "exactly three marker buckets" — adding
   `MarkerKind.TravellingVendor` in 2b-ii is purely additive.

## 10. Non-goals (this slice)

- `/map` slash command (dropped entirely).
- Per-server layer config / toggle UI (2b-ii).
- Monuments, wandering trader, oil-rig styling layers (2b-ii).
- Editing an attachment in place / hosting images on a CDN (rejected in favour of delete+repost on
  a dedicated channel).
- Player markers, vending-machine markers, explosion markers (out of scope for subsystem 2;
  vending is subsystem 8).
