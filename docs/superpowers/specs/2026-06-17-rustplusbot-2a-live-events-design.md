# Subsystem 2a — Live Events Poller + `#events` Alerts + Event `!commands`

**Status:** Design approved (2026-06-17), ready for implementation planning.
**Branch:** `feat/live-events` off `develop`.

## 1. Context

Subsystem 2 (Map + live events) is too large for one spec, so it is split along the
RustPlusApi's own seam:

- **2a (this spec)** — the live **event layer**: poll `GetMapMarkers`, diff snapshots, alert on
  marker spawn/despawn, and answer in-game "where is X now" commands. **No image rendering.**
- **2b (later)** — the **map image**: `GetMap` JPEG → ImageSharp overlays → `/map` slash variants +
  `#information` map image; plus the monument-proximity work (Explosion classification, small-vs-large
  oil-rig crate distinction).

2a is built first because it is the higher-value, lower-risk slice: alerts are the killer feature, it
reuses the established connected-loop / bus / `ICommandHandler` / channel-provisioning patterns, and it
de-risks the one genuinely new mechanic (a stateful diffing poller) before 2b adds an image pipeline.

### Verified API surface (RustPlusApi 2.0.0-beta.1, by reflection this session)

**Use the MAPPED facade (`RustPlusApi.Data.*`), not the raw `RustPlusContracts.App*` protobuf** — same as
the existing shim's `GetInfoAsync`→`Response<ServerInfo?>` / `GetTeamInfoAsync`→`Response<TeamInfo?>`.

- `RustPlus.GetMapMarkersAsync(ct)` → `Task<Response<RustPlusApi.Data.MapMarkers>>` (`.IsSuccess`/`.Data`).
  `MapMarkers` exposes **typed per-category dictionaries**, NOT a flat list: `CargoShipMarkers`,
  `PatrolHelicopterMarkers`, `Ch47Markers`, `PlayerMarkers`, `VendingMachineMarkers`,
  `TravellingVendorMarkers`, `UnknownMarkers` — each `Dictionary<ulong, XMarker>`. Each marker
  (`CargoShipMarker`/`Ch47Marker`/… all derive from `RustPlusApi.Data.Markers.Marker`) exposes only
  `Nullable<ulong> Id`, `Nullable<float> X`, `Nullable<float> Y`.
- **CRITICAL: there is NO crate bucket and NO type discriminator on `UnknownMarker`.** The current Rust
  game no longer sends locked-crate markers at all, so a crate alert is NOT achievable from
  `GetMapMarkers`. **2a therefore detects CORE-3: Cargo Ship, Patrol Helicopter, Chinook (Ch47)** — read
  straight off the three typed dictionaries. (An oil-rig-reset workaround via the CH47 is unverified and
  needs live testing — deferred.)
- `RustPlus.GetMapAsync(ct)` → `Task<Response<RustPlusApi.Data.ServerMap>>`. `ServerMap`:
  `Nullable<uint> Width/Height/OceanMargin`, `byte[] JpgImage`, `List Monuments`, `Color Background`.
  **2a uses ONLY `Width`/`Height`/`OceanMargin`** (all nullable — treat null dims as "unavailable").
- There is **no push event** for map markers (the only `On*` events are smart-switch / storage-monitor /
  team-chat / clan-chat / camera). Live events therefore **must be detected by polling + diffing**.

## 2. Scope

### In scope

- A per-connected-server **marker poll loop** (`GetMapMarkers` every 10s, configurable) inside
  `ConnectionSupervisor`'s connected window, alongside the existing heartbeat. Diffs the current marker
  set against the previous snapshot into raw added/removed deltas.
- A one-time **`GetMap` dimensions fetch** on connect (`Width`/`Height`/`OceanMargin` only — **no image
  render**) so deltas carry enough to compute grid references.
- A new **`RustPlusBot.Features.Events`** project that:
  - classifies deltas into domain events for the **core 3** marker types — **Cargo Ship, Patrol
    Helicopter, Chinook (Ch47)** (Crate dropped — the game no longer sends crate markers);
  - renders **one embed per event** to a per-server **`#events`** channel (EN/FR);
  - maintains an **in-memory active-markers + recent-event-history** store.
- Four in-game `!commands` reading that store: **`!cargo`, `!heli`, `!chinook`, `!events`**.
- Workspace provisions a per-server **`#events`** `ChannelSpec` (EN/FR name), mirroring `#teamchat`.

### Out of scope (deferred)

- All image rendering, `/map` slash commands, `#information` map image → **2b**.
- **Explosion** events and **all crate / oil-rig detection** → a later slice. The game no longer sends
  crate markers, so the only path to oil-rig-reset detection is an unverified CH47-based workaround that
  needs live testing; `!small`/`!large` and any crate alert are deferred **whole** until that is proven.
- **VendingMachine** / `TravellingVendor` markers → **subsystem 8**.
- Persisting event history across restarts. State is in-memory only; the **first poll after (re)connect
  is a silent baseline** (no false "everything just spawned" alerts).

## 3. Architecture

New project **`RustPlusBot.Features.Events`**, mirroring the existing `Features.*` pattern
(Pairing / Connections / Chat / Commands).

### Abstractions (dependency-free — no project refs, no Discord)

- `MapMarkerSnapshot(ulong Id, MarkerKind Kind, float X, float Y, string? Name)` — the diff unit.
- `MarkerKind` enum: `CargoShip, PatrolHelicopter, Chinook, Crate, Other` (the supervisor maps the raw
  `AppMarkerType` to this so it needn't know domain rules; `Other` buckets everything 2a ignores but the
  diff still tracks by `Id`).
- `MapDimensions(uint Width, uint Height, int OceanMargin)`.
- `MapMarkersChangedEvent(ulong GuildId, int ServerId, MapDimensions? Dimensions,`
  `IReadOnlyList<MapMarkerSnapshot> Added, IReadOnlyList<MapMarkerSnapshot> Removed)` — bus event.
  `Dimensions` is nullable so a failed `GetMap` degrades grid refs gracefully rather than blocking alerts.

### Connections (extend the existing seam — NO new project)

- `IRustServerConnection` (internal) gains:
  - `GetMapMarkersAsync(TimeSpan timeout, CancellationToken ct)` → `IReadOnlyList<MapMarkerSnapshot>`.
  - `GetMapDimensionsAsync(TimeSpan timeout, CancellationToken ct)` → `MapDimensions?`.
- `RustPlusSocketSource` (the untested integration shim) implements both: maps `AppMarker`→`MapMarkerSnapshot`
  (translating `AppMarkerType`→`MarkerKind`), `AppMap`→`MapDimensions`. **This is the one untested seam**,
  consistent with every prior slice; field names verified by reflection above.
- `ConnectionSupervisor.RunConnectedAsync` gains, alongside the heartbeat:
  1. **Once on window open:** `GetMapDimensions` → cache `MapDimensions?` per-socket in `_liveSockets`.
  2. **Each poll tick (`EventOptions.PollInterval`):** `GetMapMarkers` → diff against the per-socket
     previous snapshot (by `Id`) → if non-empty deltas, publish `MapMarkersChangedEvent`. **First poll =
     silent baseline** (store snapshot, publish nothing). Previous snapshot + dims live per-socket.
  3. A transient poll failure is caught/logged/skipped and **retains the previous snapshot** (so a blip
     doesn't produce a spurious despawn-then-respawn diff).
- Diff state and dims are discarded when the connected window closes (disconnect).

### Features.Events

- **`MarkerEventClassifier`** (pure) — `MapMarkersChangedEvent` → zero-or-more `RustMapEvent`:
  - `CargoShip` added → `CargoEntered`; removed → `CargoLeft`.
  - `PatrolHelicopter` added → `HeliEntered`; removed → `HeliLeft`.
  - `Chinook (Ch47)` added → `ChinookSpawned` (removal not alerted — chinooks transit briefly).
  - `Crate` added → `CrateSpawned` (generic "locked crate"; no small/large split in 2a). Removal not
    alerted.
  - `Other` → no event.
  - Each `RustMapEvent` carries the marker `X`/`Y` and the `MapDimensions?` from the bus event.
- **`GridReference`** (pure) — `From(float x, float y, MapDimensions dims)` → `"D7"`-style ref;
  returns a raw-coords fallback (`"(1234, 5678)"`) when `dims` is null. Unit-testable in isolation.
- **`RustMapEvent`** — `(MapEventKind Kind, float X, float Y, MapDimensions? Dimensions,`
  `DateTimeOffset AtUtc)`; the renderer + state store consume it.
- **`EventStateStore`** (singleton, in-memory) — per-`(guild,server)`:
  - current active markers (by kind) — drives `!cargo`/`!heli`/`!chinook`;
  - a bounded recent-event ring (~last 10) — drives `!events`;
  - `Clear(guild, server)` called on disconnect.
- **`IEventState`** (public read-seam exposed from Features.Events) — narrow query interface the command
  handlers (in Features.Commands) depend on, so the `!commands` live with all other `ICommandHandler`s
  while reading Events state. (Consistent with Commands already depending on the Connections query seam.)
- **`EventRelay`** (bus consumer) — on `MapMarkersChangedEvent`: classify → update `EventStateStore` →
  render + post one embed per `RustMapEvent` to `#events`.
- **`EventEmbedRenderer`** (pure, EN/FR via the feature's own localization catalog) — one embed per
  `RustMapEvent` (title + grid ref + timestamp).
- **`IEventChannelLocator`** + **`DiscordEventChannelPoster`** — resolve the per-server `#events` channel
  id (mirrors `ITeamChatChannelLocator`, backed by `IWorkspaceStore.GetChannelsByKeyAsync`) and post.
- **`EventsHostedService`** (thin) — the bus-subscription loop driving `EventRelay`.

### Features.Commands (existing project)

- Four new `ICommandHandler`: `CargoCommandHandler`, `HeliCommandHandler`, `ChinookCommandHandler`,
  `EventsCommandHandler` — each reads `IEventState` for `(guild, server)`. Registered in `AddCommands`
  (`CommandRegistrationTests` handler count bumped 12 → 16). EN/FR keys appended to
  `CommandLocalizationCatalog`.

### Workspace (existing project)

- New per-server `#events` `ChannelSpec` (first `ChannelPermissionProfile.Interactive` user — same profile
  as `#teamchat`) + EN/FR `channel.events.name`. Reuses existing `ProvisionedChannel` reconciliation; the
  self-heal reconciler recreates `#events` if deleted. **No new entity / migration.**

## 4. Data flow

**Connect → baseline:**

1. Supervisor opens the connected window for `(guild, server)`. Once: `GetMapDimensions` → cache dims
   per-socket. First `GetMapMarkers` → store baseline snapshot, **publish nothing**.

**Steady-state poll (every `PollInterval`, default 10s):**
2. `GetMapMarkers` → new snapshot. Diff vs. previous by `Id`: `Added` = ids newly present, `Removed` =
   ids gone. Replace previous snapshot.
3. Non-empty deltas → publish `MapMarkersChangedEvent(guild, server, dims, added[], removed[])`.

**Classification → alert + state:**
4. `EventRelay` consumes it. `MarkerEventClassifier` → `RustMapEvent`s. Each gets a grid ref via
   `GridReference.From(x, y, dims)`.
5. `EventStateStore` updated **from the full delta, independent of which deltas alert** — every `Added`
   marker enters the active set and every `Removed` marker leaves it (so an un-alerted transition, e.g. a
   crate despawning, still clears active state). Separately, each *alerted* `RustMapEvent` is pushed onto
   the bounded ring with a UTC timestamp (`IClock`).
6. `EventRelay` renders one embed per `RustMapEvent` (EN/FR) → posts to the resolved `#events` channel.

**In-game commands:**
7. `!cargo` / `!heli` / `!chinook` → read `IEventState` active marker → reply with grid ref + how-long-ago,
   or "none on the map". `!events` → the recent-event ring, newest-first.

**Disconnect:** connected window closes → per-socket snapshot + dims discarded; `EventStateStore.Clear`
for that server (so stale state can't be reported for a dead connection). Reconnect re-baselines silently.

## 5. Error handling

- Poll-loop `GetMapMarkers`/`GetMapDimensions` failures: caught per-tick, logged, **skipped**; never tear
  down the socket or heartbeat (shared connected loop). Previous snapshot retained on failure.
- `GetMapDimensions` failure on connect: dims stay null → `GridReference` raw-coords fallback until a
  later poll re-fetches; dims are static per wipe, so one success sticks.
- `EventRelay` Discord posting: each embed post wrapped in try/catch (a Discord hiccup drops one alert,
  never crashes the relay) — mirrors `TeamChatRelay`/`DiscordTeamChatWebhookPoster`.
- `#events` channel not found (not provisioned / deleted): locator returns null → relay logs-and-skips;
  workspace self-heal recreates the channel independently.

## 6. Testing

- **Pure units, fully tested:** `MarkerEventClassifier` (every core-4 add/remove → expected events,
  incl. no-op + multi-delta), `GridReference` (known X/Y→grid vectors + no-dims fallback),
  `EventEmbedRenderer` (EN/FR snapshot per event type), `EventStateStore` (active-marker upsert/clear,
  ring bound, disconnect-clear), the four command handlers (via a fake `IEventState`).
- **`FakeRustSocketSource`** gains `GetMapMarkersAsync`/`GetMapDimensionsAsync` so existing
  supervisor/connection tests still compile. (3a/3b lesson: a missing fake member silently drops a whole
  assembly — run the FULL suite + read per-assembly counts.)
- **Supervisor diff + baseline** tested against the fake: first poll silent; subsequent add/remove →
  `MapMarkersChangedEvent` published; transient poll failure retains the snapshot.
- **Untested by design:** the `RustPlusSocketSource` shim's real `AppMarker`/`AppMap` field mapping — the
  one integration seam, consistent with every prior slice.

## 7. Options & configuration

- `EventOptions { TimeSpan PollInterval = 10s }`, registered in `Program.cs` via
  `AddOptions<EventOptions>().Bind(...).Validate(...).ValidateOnStart()` (repo convention), injected into
  the supervisor poll loop.
- **No new entity / migration / EF model change** (in-memory state; `#events` reuses existing
  `ProvisionedChannel` via `GetChannelsByKeyAsync`).

## 8. Conventions / gotchas to honour

- Run `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` (the repo's real format gate)
  before pushing; the pre-push hook enforces it.
- NSubstitute on internal interfaces needs `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />`
  in the new project's csproj (as in Connections/Workspace/Pairing).
- Strict analyzers: no `// TODO` (Roslynator `S1135` = error), interface impls repeat `= default` on CT
  params (`S1006`), named-args in declaration order (`RCS1205`), XML `<param>` docs where required.
- Background-loop service + DB-poll test concurrency: use the shared-cache in-memory SQLite harness
  pattern (per-scope own connection + kept-open keep-alive), not a single shared `:memory:` connection.
- `docs/superpowers/` is **gitignored and local-only** — never commit/force-add specs or plans.
