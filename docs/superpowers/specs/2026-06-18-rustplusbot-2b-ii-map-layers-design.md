# Subsystem 2b-ii — Map Layers & Per-Server Toggles — Design

**Date:** 2026-06-18
**Branch:** `feat/map-layers` off `develop`
**Predecessor:** 2b (PR #15, merged `b6bf89c`) — base-map render in a per-server `#map` channel (ImageSharp base tile + grid + cargo/heli/chinook glyph markers).

## Summary

2b-ii completes subsystem 2's map work: it adds **per-server layer settings** with a ManageGuild **toggle UI**, four new render layers (**monuments**, **travelling vendor**, **players**, **oil-rig activation styling**), and replaces 2b's drawn glyph markers with **bundled icon assets**. All layers default **on**.

This is the settings/toggles/extra-layers slice deliberately split off from 2b, which de-risked the genuinely-new image tech first. 2b baked in forward-coupling (`MapLayerSet` value object, `MapComposer` not assuming "exactly 3 marker buckets", keyed asset thinking) so this slice needs no rework of 2b internals.

## Goals

- Persist per-`(guild, server)` map layer settings (six bool toggles, default on), surviving restart.
- Let ManageGuild admins toggle each layer from within the `#map` channel.
- Render four new layers: monuments (icons), travelling vendor (icon), players (teammate positions), oil-rig activation styling.
- Replace 2b's drawn glyph markers (C/H/K circles) with bundled icon assets for visual parity with the real Rust+ app.
- Stay within the existing architecture: no new gateway intents, no new options, reuse existing seams (`IRustServerQuery.GetMonumentsAsync`/`GetTeamInfoAsync`, 2a-ii `IRigState`).

## Non-Goals (Deferred)

- **Steam profile-picture avatars** for players (needs SteamId→avatar resolution + HTTP fetch + caching + circular composite). Players render as an icon/dot + name label this slice.
- **All-server player radar** — the Rust+ API only exposes teammates (via team info or the `PlayerMarkers` bucket, which is the connected account's own team). Players = teammates, full stop.
- The raw `MapMarkers.PlayerMarkers` API bucket — we use `GetTeamInfoAsync` instead (same data, already a wired seam, already used by `!team`/`!prox`/`!alive`).
- Per-monument **animated** icons (rustplus-desktop has chinook animation frames; out of scope).
- **Subsystem 4** smart-device map overlays (smartalarm/smartswitch/storagemonitor icons exist in the vendored set but are not drawn here).

## Architecture

Builds on `RustPlusBot.Features.Map` (2b) and `RustPlusBot.Features.Workspace`. No new project.

### 1. Data model & store

New entity **`ServerMapSettings`** in `RustPlusBot.Domain/Map/` (entities live in Domain — the `ServerCommandSettings` precedent):

```csharp
public sealed class ServerMapSettings
{
    public ulong GuildId { get; set; }
    public Guid ServerId { get; set; }       // PK, FK -> RustServer (cascade)
    public bool ShowGrid { get; set; } = true;
    public bool ShowMarkers { get; set; } = true;     // cargo/heli/chinook
    public bool ShowMonuments { get; set; } = true;
    public bool ShowVendor { get; set; } = true;      // travelling vendor
    public bool ShowPlayers { get; set; } = true;     // teammate positions
    public bool ShowRigs { get; set; } = true;        // oil-rig activation styling
}
```

- `ServerId` is the primary key (one row per server), FK→`RustServer` with **cascade delete** (mirrors `ServerCommandSettings`, 1b-iii, 3b). Removing a server cleans its map settings.
- `IEntityTypeConfiguration<ServerMapSettings>` class for mapping; global ulong↔long snowflake conversion (repo convention).
- Migration **`MapSettings`**.

New store **`IMapSettingsStore`** + `MapSettingsStore` in `RustPlusBot.Persistence/Map/` (mirrors `MuteStore`):

```csharp
public interface IMapSettingsStore
{
    // Returns the persisted layer set, or all-on defaults when no row exists.
    Task<MapLayerSet> GetAsync(ulong guildId, Guid serverId, CancellationToken ct = default);
    // Read-or-create the row, flip one layer, save.
    Task SetLayerAsync(ulong guildId, Guid serverId, MapLayer layer, bool enabled, CancellationToken ct = default);
}
```

`MapLayer` is a small enum (`Grid, Markers, Monuments, Vendor, Players, Rigs`) so `SetLayerAsync` takes a typed layer rather than a string. `GetAsync` maps the entity to the `MapLayerSet` value object (defaults-on when the row is absent).

### 2. `MapLayerSet` extension

`MapLayerSet` (in `Features.Map/Rendering/`) gains a sixth field **`Players`**, so the store maps 1:1:

```csharp
public sealed record MapLayerSet(bool Grid, bool Markers, bool Monuments, bool Vendor, bool Players, bool Rigs)
{
    public static MapLayerSet AllOn { get; } = new(true, true, true, true, true, true);
}
```

The 2b `Default2b` constant is **removed**: the `MapComposer` reads `IMapSettingsStore.GetAsync` for the live per-server set, and the store returns `AllOn` when no row exists (so a freshly-provisioned server still renders all layers). `AllOn` is the single defaults-on source of truth, used by both the store fallback and `MapComposer`.

### 3. Icon assets (replace drawn glyphs)

Vendor the needed PNGs from `/home/handys11/Dev/rustplus-desktop/RustPlusDesktop/Assets/icons/` into `RustPlusBot.Features.Map/Assets/` as **embedded resources** (`<EmbeddedResource>` in the csproj). A keyed **`MapIcons`** registry loads each once via `GetManifestResourceStream` (mirrors `MapRenderer.LoadFont`):

- **Markers:** `cargo.png` (CargoShip), `patrol.png` (PatrolHelicopter), `ch47.png` (Chinook), `vendor.png` (TravellingVendor), `player.png` (Players).
- **Monuments:** the monument icon set (airfield, arcticresearch, banditcamp, dome, excavator, ferryterminal, fishingvillage(+large), gasstation, harbour(+2), hqmquarry, junkyard, largeoilrig, launchsite, lighthouse, militarybase, militarytunnel, miningoutpost, missilesilo, oilrig, outpost, powerplant/powerstation, satellitedish, sewerbranch, stonequarry, sulfurquarry, supermarket, swamp, trainyard, traintunnel, watertreatment, waterwell, stable, …).

`MapIcons.Marker(MarkerKind) → Image?` and `MapIcons.Monument(token) → Image?` (null = no icon → caller skips). `MarkerGlyphs` (2b's drawn C/H/K circles) is **replaced** by icon draws.

**`MonumentIconMap`** maps the Rust+ protobuf monument **token** → vendored icon filename. Token list derived from rustplusplus `monumentInfo` (e.g. `airfield_display_name`, `dome_monument_name`, `launchsite`, `bandit_camp`, `large_oil_rig`, `oil_rig_small`, `water_treatment_plant_display_name`, …). **Unknown tokens are skipped** (the decided stance — companion-app behaviour: render what you recognise).

> **Verify-point (token naming):** rustplusplus lists the small rig as `oil_rig_small`, but 2a-ii pinned `oilrig_1` / `large_oil_rig` against the real 2.0.0-beta.1 protobuf for rig *detection*. The live token comes from `ServerMapMonument.Name`. During execution, confirm the actual emitted tokens and map both observed spellings. The rigs layer (below) must use the **same** tokens 2a-ii's `MonumentsResult` filter uses.

### 4. New render layers (`MapRenderer` + `MapComposer`)

`MapRenderer.Render(...)` keeps its signature shape but the layer-conditional blocks expand. `MapComposer.ComposeAsync` becomes the **gather point**: read settings → conditionally gather per enabled layer → project to pixels → render.

- **Markers** (existing, re-skinned): cargo/heli/chinook from `IEventState.GetActiveMarkers`, drawn with bundled icons. `MapComposer.DrawnKinds` keeps the three when `Markers` on.
- **Monuments:** **new** `IRustServerQuery.GetMonumentsAsync(guild, server, ct)`. 2a-ii fetches monuments once on connect inside `ConnectionSupervisor` (internal `IRustServerConnection.GetMonumentsAsync`) and consumes them transiently for rig detection — they are **not** queryable. This slice adds `GetMonumentsAsync` to the public `IRustServerQuery` seam, implemented by `ConnectionSupervisor` over its live socket (mirrors `GetTeamInfoAsync`). For each monument, `MonumentIconMap` token→icon; skip unknown; composite at world→pixel coords.
- **Vendor:** new `MarkerKind.TravellingVendor` enum member; extend `RustPlusSocketSource.GetMapMarkersAsync` to also surface `MapMarkers.TravellingVendorMarkers` (currently only cargo/heli/chinook via the `AddMarkers` helper — add one more `AddMarkers(... data.TravellingVendorMarkers, MarkerKind.TravellingVendor)` line). Drawn with `vendor.png`. Composer adds `TravellingVendor` to `DrawnKinds` when `Vendor` on.
- **Players:** `IRustServerQuery.GetTeamInfoAsync(guild, server)` → `TeamInfoSnapshot.Members` (each has `X, Y, Name, SteamId, IsOnline, IsAlive`). Drawn at `(X, Y)` with `player.png` (or a coloured dot) + name label. Dead/offline members styled differently (greyed / smaller / "dead" suffix). **Not** routed through the marker pipeline — a separate gather in the composer. Avatars deferred.
- **Rigs:** reuse 2a-ii `IRigState.Get(guild, server, RigKind)`. Map the rig monument tokens to `RigKind` using the **same tokens 2a-ii pinned against the real protobuf**: `oilrig_1` → `RigKind.Small`, `large_oil_rig` → `RigKind.Large` (rustplusplus's `oil_rig_small` spelling is NOT what the live API emits — `ConnectionSupervisor.GetRigPositionsAsync` confirms `oilrig_1`/`large_oil_rig`). Overlay activation styling on the rig icon (e.g. tint/badge when `Active` vs `Online`). Rig icons render even if the general monuments layer is off (rig is its own draw, gated by `Rigs`).

`WorldToPixel.ToPixel` (2b) is reused for all coordinate projection. Icons composited centered at a fixed display size (~24px), tunable constant in `MapRenderer`.

### 5. Toggle UI (Approach A — control message as a Workspace `MessageSpec`)

The `#map` channel holds **two** bot messages:

1. The **delete+reposted image** (2b's `DiscordMapChannelPoster`).
2. A new **persistent control message** (toggle buttons), reconciled by the existing Workspace `MessageSpec`/reconciler machinery (find-or-create + self-heal), exactly like `#info`'s managed message.

- New `WorkspaceMessageKeys.ServerMap` `MessageSpec` (per-server, `#map` channel) registered in `ServerWorkspaceSpecProvider.GetMessageSpecs()`.
- New **`MapControlMessageRenderer`** (in Workspace, beside `ServerInfoMessageRenderer`) builds the control message: a short header + **six ManageGuild toggle buttons** (Grid / Markers / Monuments / Vendor / Players / Rigs), each labelled with current on/off state and styled (green = on / grey = off), reading `IMapSettingsStore` + the Workspace localizer (EN/FR labels in the Workspace `LocalizationCatalog`).
- New **`MapComponentModule`** (Workspace) — wildcard `[ComponentInteraction("map:toggle:*")]`, `[RequireUserPermission(GuildPermission.ManageGuild)]`, custom-id `map:toggle:{layer}:{serverId}`. Mirrors `ConnectionComponentModule`: defer-ephemeral → scope → parse layer+serverId → `IMapSettingsStore.SetLayerAsync` flip → re-render the control message (reconcile that server) → trigger an immediate map repaint. Lives in Workspace (renderer + reconciler are there → no new cross-feature dep).

> **Forged-payload safety** (the `SettingsComponentModule` precedent): validate the parsed `layer` against the known enum and `serverId` against a real provisioned server before persisting.

### 6. Poster: don't delete the control message

`DiscordMapChannelPoster.DeletePriorBotMessagesAsync` currently deletes **all** recent bot messages in `#map`. It must skip the managed control message. **Rule: delete only prior bot messages that carry a file attachment** (the image posts); the control message is text+components with no attachment, so it survives. Precise, self-contained, no new coupling to Workspace.

### 7. Immediate repaint on toggle (bus event)

Toggling a layer should refresh the image now, not wait up to `MapRefreshInterval` (~45s). Map references Workspace (not the reverse), so Workspace's `MapComponentModule` cannot call into `MapHostedService` directly. Instead, the toggle module publishes a new lightweight **`MapSettingsChangedEvent(GuildId, ServerId)`** (Abstractions, ns `RustPlusBot.Abstractions.Events`) on the in-process bus; `MapHostedService` adds a fourth consumer loop (alongside `MapMarkersChangedEvent`/`ConnectionStatusChangedEvent`/the tick) that calls the existing `RefreshAsync(guild, server, ct)` — reusing the existing `MapRefreshThrottle` so a rapid series of toggles coalesces into one repaint. This is the idiomatic in-process trigger here (same shape as the existing marker-event loop).

## Data Flow

```
Toggle click (ManageGuild)
  → MapComponentModule (Workspace): SetLayerAsync flip
  → reconcile control message (new labels) + trigger Map repaint
  → MapHostedService repaints server (throttled)
  → MapComposer.ComposeAsync:
       read IMapSettingsStore -> MapLayerSet
       gather per enabled layer:
         markers   (IEventState)        if Markers
         vendor    (IEventState/new kind) if Vendor
         monuments (IRustServerQuery.GetMonumentsAsync, MonumentIconMap) if Monuments
         players   (IRustServerQuery.GetTeamInfoAsync) if Players
         rigs      (IRigState + rig tokens) if Rigs
       project via WorldToPixel
  → MapRenderer.Render(base, dims, placements, layerSet) -> PNG
  → DiscordMapChannelPoster.PostAsync (delete prior *attachment* messages, repost image)
```

## Testing (TDD, mirrors 2b `Features.Map.Tests`)

- **`MapSettingsStore`** (Persistence tests): defaults-on when no row; set creates row; set updates row; per-layer independence. Seed a `RustServer` first (FK cascade — the 1b-iii/3b orphan-row trap).
- **`MapIcons` / `MonumentIconMap`**: known token resolves to an icon; unknown token → null (skipped); all bundled icons load from embedded resources (no missing-resource throw).
- **`MapComposer`**: each layer gathered only when its toggle is on; players/rigs/vendor/monuments placements projected to expected pixels; all-on fallback when no settings row; no-dims path renders base tile only (2b behaviour preserved).
- **`MapControlMessageRenderer`**: six buttons present, correct on/off label + style per setting, EN/FR.
- **`MapRenderer`**: new layers draw (smoke-level — valid PNG, no throw with icon/team/rig inputs), consistent with 2b's renderer tests.
- **Modules** (`MapComponentModule`): not unit-tested (`InteractionModuleBase` convention) — covered at the store + renderer level.

## Untested Integration Shims (repo convention)

- `RustPlusSocketSource.GetMapMarkersAsync` — the added `TravellingVendorMarkers` bucket.
- `DiscordMapChannelPoster` — the skip-attachmentless-message delete change.
- (`GetMonumentsAsync` / `GetTeamInfoAsync` already exist and were exercised by 2a-ii / 3b-ii.)

## Risks & Gotchas

- **EF drift / FK:** new entity + migration `MapSettings` + cascade FK. Any test persisting `ServerMapSettings` must seed `RustServer` first. Run the full suite + read per-assembly counts (the recurring "fake/store change silently drops an assembly's tests" trap). Verify no-drift the 2a-ii way if `ef migrations has-pending-model-changes` errors on tooling.
- **Workspace count tests:** new `MessageSpec` (+ no new channel) — bump any Workspace test asserting message-spec counts.
- **Two managed messages in `#map`:** the control message (reconciler-owned) and the image (poster-owned) must not fight. The attachment-based delete rule is the contract; verify the reconciler's find-or-create does not match the image message (it shouldn't — different message key/content).
- **Monument token spelling** (verify-point above): align the rigs layer tokens with 2a-ii's detection tokens.
- **jb `ReformatAndReorder`** is the real format gate (run `dotnet jb cleanupcode --profile=ReformatAndReorder` before pushing); Roslynator strict analyzers (`S1135` TODO-as-error, `RCS1141` param docs, `CA1822`, `CA1031` broad-catch pragmas) per prior slices.
- **Icon licensing** (decided, recorded here): the rustplus-desktop icons are **Facepunch/Rust game art** the GPL-3.0 repo merely redistributes — the icons are not that project's original GPL work, so its GPL does not attach to the third-party game art. This MIT repo bundles them as embedded resources, attributes origin as Facepunch/Rust game art (companion-app fair use, same as the official Rust+ app and both reference bots), and does **not** treat them as GPL nor as encumbering the MIT licence. Record this in `NOTICE` alongside the existing font attribution.

## Asset Provenance

Icons vendored from `/home/handys11/Dev/rustplus-desktop` (`RustPlusDesktop/Assets/icons/`). Monument token→display-name reference derived from `/home/handys11/Dev/rustplusplus` (`src/structures/Map.js` `monumentInfo`).
