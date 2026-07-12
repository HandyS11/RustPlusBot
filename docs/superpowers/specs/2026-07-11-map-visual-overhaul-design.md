# Map visual overhaul — design

**Date:** 2026-07-11
**Status:** Approved direction, pending spec review
**Surface:** `#map` channel image (`src/RustPlusBot.Features.Map/`)

## Context

The `#map` renderer draws overlay layers onto a 1024px Rust+ base tile
(`MapRenderer`, ImageSharp) and posts `map.png` to the `#map` channel. Four visual
problems, reported by the server owner:

1. **Event icons are poor.** The patrol-heli icon is an ornate red 3/4-view
   illustration that reads as noise from top-down; the CH47 rotor is mis-placed; the
   travelling-vendor icon is visibly broken (green blobs around a cart).
2. **Motion trails are ugly.** A solid fading polyline is drawn behind every moving
   marker (`DrawTrails`) — "trailing pixels in the form of a line."
3. **Train tunnels can't be toggled.** The `train_tunnel_display_name` and
   `train_tunnel_link_display_name` tokens are baked into the Monuments layer; there is
   no way to show/hide them independently.
4. **Player display is a mess.** Every teammate is the same green circle with a white
   name label drawn on the map; no per-player identity, cluttered labels, no legend.

Base facts established during exploration:

- Layers are per-server toggles persisted via `IMapSettingsStore` / `MapLayerSettings`
  and controlled by six buttons in `MapControlMessageRenderer` (rows 0–2).
- Marker icons load from embedded PNGs in `Assets/icons/` via `MapIcons.KeyFor`; they
  are rotated to `marker.Rotation` in `DrawMarkers`.
- Monuments (incl. tunnels) render from RustMaps SVG art rasterised at runtime by
  `MonumentIconSource`; token→type mapping lives in `MonumentTokenMap`.
- Trail history is capped at 6 points (`EventStateStore.HistoryCapacity`); per-kind
  trail colors/width live in `MapRenderStyle`.
- `TeamMemberSnapshot` carries a stable `SteamId` (Steam64), `Name`, `X/Y`, `IsOnline`,
  `IsAlive` — used for stable per-player color assignment.
- The image poster (`DiscordMapChannelPoster`) sends `map.png` as a bare file
  attachment (no text/embed) and deletes only prior *image* messages, leaving the
  persistent control message alone.

## Goals

- Clean, correct top-down event icons with rotors rendered as real, correctly-placed
  elements (heli = 1 rotor, CH47 = 2 tandem rotors).
- Trails kept but visually subtle.
- A separate, persisted, toggleable **Tunnels** layer.
- Per-player colored crosses (no on-map names) plus a color→player→status **legend**
  attached to the map image message as a Discord embed.

## Non-goals

- No animation (the map is a static PNG per refresh; rotors are drawn at a fixed
  representative angle).
- No change to the base-map source, projection, grid, or refresh/throttle machinery.
- No new player-tracking data; we only restyle what `GetTeamInfoAsync` already returns.

---

## A. Event icons — real, correctly-placed rotors

**Approach:** composite body + rotor(s) as separate elements (matching the rustplusplus
reference art), then let the existing rotation path rotate the finished composite to
heading. Rotating the whole composite keeps rotor positions aligned with the body.

**Assets** (`src/RustPlusBot.Features.Map/Assets/icons/`, embedded via the existing
`Assets/icons/*.png` glob):

- Add `heli.png` (grey body, no rotor), `chinook.png` (grey body), `blade.png` (3-blade
  rotor), all from `rustplusplus/src/resources/images/markers/`.
- Overwrite `vendor.png` with the reference `shop.png` (clean cart) — keeps the
  `vendor` key unchanged.
- Keep `cargo.png` (already identical to the reference bullet).
- Delete `patrol.png` and `ch47.png` (superseded by composites).
- Copying game-derived marker art is consistent with the project's existing icon stance
  (already vendoring `cargo.png`).

**`MarkerIconComposer`** (new, in `Assets/`): builds two cached composites at native
resolution:

- **Patrol heli** = `heli.png` + one `blade.png` centred on the main-rotor hub.
- **CH47** = `chinook.png` + **two** `blade.png`, at the front and rear tandem-rotor
  positions.

Blade placement offsets (hub position, rear/front offset, blade scale) are named
constants, tuned against the reference art and verified by rendering a test tile. Both
blades drawn at a fixed angle (rear may be offset ~30° for visual variety).

**`MapIcons`**: `Marker(PatrolHelicopter)` / `Marker(Chinook)` return the composer's
cached composites; `cargo` / `vendor` load directly. `DrawMarkers` is unchanged — it
scales (`MapRenderStyle.MarkerIconSize`) and rotates the composite as today.

## B. Trails — keep but subtle

`DrawTrails` stays but is toned down so it reads as a faint direction hint:

- Thinner stroke and a lower opacity ceiling (tunable in `MapRenderStyle`).
- Drawn as a short **dashed/dotted** polyline instead of a solid fading line.
- Trail length (6 points) and per-kind base colors unchanged.

New/changed constants live in `MapRenderStyle` (`TrailWidth`, a new opacity ceiling, a
dash pattern). No data-path change.

## C. Train tunnels — new toggle layer (RustMaps SVG art)

Mirror the existing layer pattern; keep the current RustMaps SVG icons, just move the
two tunnel tokens onto a separately-toggleable layer.

**Persistence** (`RustPlusBot.Persistence/Map/`):

- `MapLayer.Tunnels = 6`.
- `Tunnels` bool on `MapLayerSettings` (defaults **on**; `AllOn` includes it).
- EF entity column + migration (schema version bump); `MapSettingsStore` read/write and
  `SetLayerAsync` handle the new column.

**UI** (`MapControlMessageRenderer`):

- A 7th toggle button (`map.layer.tunnels`) added to an existing row (row 1 goes to four
  buttons; Discord allows five).
- New EN/FR localization strings `map.layer.tunnels`.

**Rendering / routing** (`MapComposer`, `MapRenderer`):

- Define `TunnelTokens = { "train_tunnel_display_name", "train_tunnel_link_display_name" }`
  (a shared constant).
- `GatherMonuments` **excludes** tokens in `TunnelTokens`.
- New `GatherTunnels` collects those tokens when `layers.Tunnels` is on, producing
  placements resolved through the *same* `MonumentIconSource.Monument(token, size)` (so the
  icons are identical, just gated separately).
- `MapRenderer` draws the tunnel placements in their own layer pass (structurally a
  clone of `DrawMonuments`). `MapLayerSet` gains `Tunnels`.
- Trainyard and Military Tunnels remain ordinary monuments (unaffected).
- `MonumentTokenMap` keeps its entries (routing happens in the composer); drift-guard
  tests updated to reflect that the tunnel tokens are now owned by the Tunnels layer.

## D. Players — colored crosses + legend embed

**On-map marker:** replace the green `player.png` circle with a drawn cross:

- **Shape by liveness:** `+` for alive, `x` for dead.
- **Opacity by presence:** full when online, dimmed (~0.5) when offline.
- Dark ~1px halo behind the colored stroke for contrast on any base tile.
- **No on-map name label** (`DrawPlayerLabel` removed).
- `player.png`, `MapIcons.Player`, and `PlayerIconSize`-as-circle are removed; a
  `PlayerCrossSize`/stroke width lives in `MapRenderStyle`.

**Stable per-player color** — `PlayerPalette` (new, single source of truth):

- Nine entries, each an `(ImageSharp Color Rgba, string Emoji)` matching Discord's
  colored squares: 🟥 🟧 🟨 🟩 🟦 🟪 🟫 ⬛ ⬜.
- Assignment: sort the team's members by `SteamId` ascending, index into the palette
  (wrap if >9). `SteamId` is stable, so a player keeps their color across refreshes and
  regardless of online/offline ordering.

**Legend** — a Discord embed on the map image message:

- `MapLegend` (plain model in the Map feature): ordered entries of
  `(Emoji, Name, StatusText)`. Status wording: `online` / `offline`, with `, dead`
  appended when `!IsAlive` (e.g. `online`, `offline, dead`).
- Built in `MapComposer` from the same palette assignment used for the crosses.
- Rendered as an embed with one description line per player:
  `🟥 **Alice** — online`.
- **No embed** when the Players layer is off or the team is empty.

## Plumbing & data flow

- `PlayerPlacement` gains the resolved cross `Color` (and effectively its liveness, which
  it already carries).
- `MapComposer.ComposeAsync` returns `MapComposition(byte[] Png, MapLegend? Legend)`
  instead of raw `byte[]`.
- `IMapChannelPoster.PostAsync` gains a `MapLegend?` parameter; `DiscordMapChannelPoster`
  builds the Discord `Embed` internally (Discord types stay in the poster) and passes it
  to `SendFileAsync(..., embed: ...)`. Delete-prior-image behavior is unchanged, so the
  legend embed is reposted atomically with each image.
- `MapHostedService.RefreshAsync` threads `Png` + `Legend` from composer to poster.

```
GetTeamInfoAsync ─► MapComposer: assign PlayerPalette by SteamId
                       ├─► PlayerPlacement(+Color)  ─► MapRenderer.DrawPlayers (cross)
                       └─► MapLegend(emoji,name,status)
MapComposer.ComposeAsync ─► MapComposition(png, legend)
MapHostedService.RefreshAsync ─► Poster.PostAsync(channel, png, legend)
                                     └─► SendFileAsync(map.png, embed: legend)
```

## Testing

- **Icons:** `MarkerIconComposer` produces a heli composite with 1 rotor and a CH47
  composite with 2 rotors; blade offsets non-overlapping; composites cached (same
  instance on repeat calls).
- **Trails:** width/opacity within the new subtle bounds (constant-level assertions).
- **Players:** cross drawn in the assigned palette color; `+` for alive vs `x` for dead;
  dimmed alpha when offline; no name text emitted.
- **Palette/legend:** SteamId→color mapping is stable and order-independent; legend
  entries + status wording correct; empty team / players-off ⇒ no legend.
- **Tunnels routing:** tunnel tokens excluded from the monuments list and included in the
  tunnels list only when the layer is on; Trainyard/Military Tunnels unaffected.
- **Persistence:** `Tunnels` column round-trips, defaults on, `SetLayerAsync(Tunnels,…)`
  persists; migration applies cleanly.
- **Localization:** `map.layer.tunnels` present in EN and FR (drift-guard).
- Build + `dotnet jb cleanupcode --profile=ReformatAndReorder` clean (hard CI gate).

## Risks & sequencing

- **DB migration** (Tunnels column) and **poster/composer signature changes** (legend)
  are the higher-risk items; icons and trails are low-risk visual tweaks.
- Suggested implementation order (single PR): (1) event icons, (2) trails, (3) Tunnels
  layer end-to-end, (4) players + legend + poster/composer rework.
- Blade offsets and cross sizing need a visual pass on a real rendered tile before the PR
  is considered done (live `#map` smoke).
- Palette caps at 9 colors; teams >9 wrap (documented, acceptable).

## Out-of-scope follow-ups (not in this spec)

- Animated rotors, per-player avatars, death-note marker styling, trail length tuning.
