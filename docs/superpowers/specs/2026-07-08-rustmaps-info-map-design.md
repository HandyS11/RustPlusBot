# RustMaps #info Static Map + Auto-Generate — Design

**Date:** 2026-07-08
**Branch:** `feat/map-render-rework` (additive slice on the same branch)
**Status:** Approved by user (brainstorm 2026-07-08)
**Depends on:** RustMapsApi `1.0.0-beta.2` (already bumped, commit b18fb59)
**Supersedes:** the earlier `2026-07-08-rustmaps-auto-generate-design.md` (base-map approach — abandoned).

## Problem & pivot

The branch made RustMaps the *preferred base map* for the layered #map render. That is wrong: the
#map render is a tool with **user-toggleable layers** that draws its **own** icons on a neutral
terrain base — pulling RustMaps' pre-rendered map (icons/grid baked in) as that base fights the
design. So: **the #map layered render reverts to the Rust+ server tile only.**

RustMaps is still valuable as a standalone, high-quality **static map picture for everyone** — so it
moves to a new surface: a static map embed in the per-server **#info** channel. When RustMaps does
not yet have the map, generate it (credits, minutes) in the background and swap it in; until then (or
if generation fails / credits are exhausted) show a **bot-rendered static fallback** that replicates
RustMaps as closely as our pipeline allows.

## Decisions (from brainstorm)

- **#map base map:** Rust+ server tile only (RustMaps removed from the base-map chain).
- **RustMaps surface:** a static map image posted to the #info channel.
- **Image:** RustMaps `ImageUrl` (icons + grid baked — the full readable map).
- **Auto-generate:** reused, credit-safe — pre-check `GetLimitsAsync` (fail-closed), `CreateMap`
  **once** per `(size, seed)`, poll, no retry on failure.
- **Fallback (RustMaps generating / failed / limit-reached):** our own render of the Rust+ base with
  **only static layers — grid + monuments on; players, markers (cargo/heli/chinook), vendor, events,
  trails all off.**
- **Delivery:** the #info map is an **image attachment** (mirrors the #map image poster), NOT a
  reconciled embed — because `MessagePayload` carries no attachment and the fallback is our own bytes.
  So both the RustMaps image (downloaded once per wipe) and the fallback render are delivered as an
  attachment + embed via delete+repost.
- **Gating:** the whole surface is gated behind `Map:RustMaps:ApiKey` (no key → no #info map).
- **Wipe detection:** OUT OF SCOPE (dropped — a cross-cutting concern for a wider, separate effort).

## Part A — Revert RustMaps from the base map

- Delete `Composing/RustMapsBaseMapSource.cs` and `tests/…/RustMapsBaseMapSourceTests.cs`.
- `MapServiceCollectionExtensions.AddMap`: stop registering `RustMapsBaseMapSource` as
  `IBaseMapSource`. The base-map chain is `RustPlusBaseMapSource` only. Keep `AddRustMapsClientV4`
  and the named `HttpClient` (`HttpClientName` moves to the new poster/service — it now downloads the
  RustMaps `ImageUrl` once per wipe for the #info attachment).
- `IBaseMapSource` / `BaseMapCache` / `BaseMapImage` and the per-image projection metadata stay
  unchanged (still correct; chain simply has one source).
- `MapRegistrationTests`: base chain resolves to `RustPlusBaseMapSource` only; the new RustMaps
  components register **iff** the API key is present.

## Part B — #info static map (attachment delivery)

All new code in `RustPlusBot.Features.Map` except the channel locator (Workspace, mirroring the
existing per-channel locators).

### State + coordinator

- `RustMapsGenerationState` (enum): `Idle`, `Generating`, `Ready`, `Failed`, `LimitReached`
  (last three terminal for the current wipe).
- `RustMapsMapKey(int Size, int Seed)` (record) — the map's identity, shared across servers on a wipe.
- `IRustMapsMapCoordinator` + `RustMapsMapCoordinator` (singleton, in-memory): per key holds state,
  the `MapId` once assigned, the cached ready `MapInfo` (`ImageUrl`, `Url`), and the set of requesting
  `(ulong Guild, Guid Server)`. `Register(size, seed, guild, server)` is idempotent (adds requester,
  creates `Idle` on first sight, never re-triggers spend, never resets a terminal state). Accessors:
  `TrySetGenerating(key, mapId)`, `SetReady(key, info)`, `SetFailed(key)`, `SetLimitReached(key)`,
  `Snapshot(key)`, enumerate pending keys, requesters-for-key. Thread-safe (per `EventStateStore` style).

### `InfoMapHostedService : IHostedService`

Registered only when the API key is present. Owns the whole #info map lifecycle:

- **Connection tracking:** subscribe to `ConnectionStatusChangedEvent`; maintain a `_connected` set
  (like `MapHostedService`). On connect: resolve world (`GetWorldAsync` → size, seed) →
  `coordinator.Register(...)` → **post the fallback static image** to #info immediately (so the channel
  shows a map at once). On disconnect: remove from `_connected` (leave the last post in place).
- **Timer loop** at `MapOptions.RustMaps.GenerationPollInterval` (default 20s):
  1. **Advance `Generating`:** poll `GetMapByIdAsync(mapId)`; `State == Active` → `SetReady` (below).
  2. **Start `Idle`:** `GetMapBySeedAndSizeAsync` — success + `ImageUrl` → `SetReady`. `NotFound` →
     `GetLimitsAsync` (a stat blocks only when present and `Current >= Allowed`; null stat is
     non-limiting; **the `GetLimits` call failing → `SetFailed`, no spend** — fail closed). If OK →
     `CreateMapAsync(new { Size, Seed, Staging = false })` once → `TrySetGenerating`. In-progress
     (`Queued`) → poll, no `CreateMap`.
  3. **On `SetReady`:** download the RustMaps `ImageUrl` bytes once (named `HttpClient`), cache in the
     coordinator, and **repost** the #info image (RustMaps bytes) + a metadata embed for each connected
     requester. Terminal — no further work / no re-spend.
  4. Any `CreateMap`/poll/limits/download error → `SetFailed`/`SetLimitReached` (log, no retry); the
     fallback image already posted stays.
- Broad-catch per iteration; `OperationCanceledException` propagates only on real shutdown
  (`when (token.IsCancellationRequested)`).

### `MapComposer.ComposeStaticAsync(guild, server, ct)`

New method: composes the Rust+ base with a **fixed** `MapLayerSet(Grid: true, Markers: false,
Monuments: true, Vendor: false, Players: false, Rigs: false)` — bypassing the per-server #map toggle
settings. Reuses `BaseMapCache` (fetches + caches the base on demand). Returns PNG bytes, or null when
no base map is available yet (service retries next tick). This is the fallback image.

### Delivery — poster + locator (mirror the #map pattern)

- `IInfoChannelLocator` + `InfoChannelLocator` (Workspace, `Locating/`): resolves the per-server
  **#info** channel id (`WorkspaceChannelKeys.ServerInfo = "info"`), a mirror of `MapChannelLocator`
  over `CachingChannelLocator`.
- `IInfoMapPoster` + `DiscordInfoMapPoster` (Features.Map, `Posting/`): `PostAsync(channelId, embed,
  pngBytes, ct)` — deletes the bot's prior **image** posts in #info (scan last N, only `Attachments >
  0`, so the reconciled status embed survives) then `SendFileAsync(png, "map.png", embed)`. A mirror of
  `DiscordMapChannelPoster` (untested integration shim). The embed carries title, size, seed, and —
  when RustMaps is ready — the RustMaps page link (`MapInfo.Url`).
- **Coexistence:** the #info channel now holds two bot messages — the reconciled `ServerInfo` status
  embed (Workspace, edited by persisted id) and this image post. The plan MUST verify the Workspace
  reconciler edits/prunes only messages it owns by `ProvisionedMessage.DiscordMessageId` and does not
  delete this foreign image post (this is exactly how the #map control message + #map image coexist —
  reuse that guarantee). If the reconciler prunes unknown messages, adjust so the image survives.

### Configuration

- Reintroduce `RustMapsOptions` as `MapOptions.RustMaps` carrying `GenerationPollInterval`
  (`TimeSpan`, default `00:00:20`, `ValidateOnStart` `> TimeSpan.Zero`). The API key is still read via
  `configuration["Map:RustMaps:ApiKey"]` at registration. `appsettings.json` gains the poll interval
  under `Map:RustMaps`.

### Localization

Embed strings (title, "generated on RustMaps"/"map preview" caption, size/seed/link field labels) in
the shared `Strings.resx`/`Strings.fr.resx` via `ILocalizer` (per the repo's shared-resx convention).

## Data flow

```text
connect → InfoMapHostedService:
    Register(size,seed,guild,server) + ComposeStaticAsync → post fallback (Grid+Monuments) to #info
timer loop (20s):
    Idle → GET → exists? SetReady(info) : NotFound → GetLimits (fail-closed) → CreateMap once → Generating(mapId)
    Generating → poll GetMapById → Active → SetReady(info)
    SetReady → download ImageUrl bytes (once) → repost #info image (RustMaps) + link embed, per requester
    Failed / LimitReached → keep the already-posted fallback, no retry
disconnect → drop from _connected
```

## Error handling

- RustMaps unavailable at any step → the #info map shows the static fallback (already posted); it is
  never empty (given the API key + a reachable base map).
- Limits exhausted → `LimitReached`; `GetLimits` call fails → `Failed` (no spend); `CreateMap`/poll/
  download error → `Failed`. All: log once, no retry, fallback stays.
- No API key → the whole surface is inactive (no #info map post at all — today's behavior).
- Base map not yet fetched on connect → fallback post skipped that tick, retried next tick.

## Testing (xUnit + NSubstitute + fake `IClock`)

- **Coordinator:** `Register` idempotent; multiple servers sharing `(size,seed)` accumulate as
  requesters under one key; state transitions; terminal states not reset; ready `MapInfo` cached.
- **Service:** connect → `Register` + fallback post; limits-exhausted → `CreateMap` never called +
  `LimitReached`; **`GetLimits` failure → `CreateMap` never called + `Failed`** (fail closed);
  `CreateMap` called **exactly once** per key across many ticks and multiple requesters; `Generating`
  → poll → `Active` → download + repost RustMaps image per requester; `CreateMap`/poll error →
  `Failed`, no retry; disconnect → dropped.
- **`ComposeStaticAsync`:** produces a PNG with grid + monuments and **without** players/markers/
  vendor/trails (assert against a marker-bearing state that the dynamic pixels are absent, e.g. a
  changed-pixel-bounds check vs a full render); null when no base map.
- **`DiscordInfoMapPoster`:** deletes only prior attachment posts (a non-attachment message survives);
  reposts image + embed. (Shim — light coverage per repo convention.)
- **`InfoChannelLocator`:** resolves the "info" channel id; null when unprovisioned.
- **Registration:** key present → coordinator + service + poster + locator resolve; key absent → none
  registered; base-map chain resolves to `RustPlusBaseMapSource` only.

## Global constraints (carried from the branch)

`RustPlusBot.slnx`; `dotnet build -warnaserror -maxcpucount:1` (maxcpucount:1 mandatory — git-hooks
race drops assemblies otherwise); xUnit `Assert.*` + NSubstitute, no FluentAssertions; XML docs on
public members; `InvariantCulture`/`Ordinal`; ImageSharp 3.1.12 / Drawing 2.1.7 unchanged;
`docs/superpowers/` gitignored.

## Out of scope (YAGNI)

Wipe / seed-size-change detection (dropped — wider cross-cutting effort); persistence of generation
state (re-derived from GET on restart); reconciled-message delivery (attachment poster used instead);
showing the #info map without a RustMaps key; `RawImageUrl`/`ThumbnailUrl` variants; org (`orgId`)
scoping; custom-map configs.
