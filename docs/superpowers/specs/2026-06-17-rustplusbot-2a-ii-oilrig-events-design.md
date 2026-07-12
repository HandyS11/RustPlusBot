# Subsystem 2a-ii — Oil Rig Activation Detection (small / large)

Status: design approved 2026-06-17
Builds on: subsystem 2a (live events — PR #13, merged `d4712b8` on `develop`)
Branch to use: `feat/oilrig-events` off `develop`

## 1. Problem & intel

The bot should report the status of the **small** and **large** oil rigs and fire an
event when a rig activates. The reference bot exposed this via crate map markers, but the
**current Rust game no longer sends locked-crate map markers** over Rust+
(see `rustplusapi-map-markers-no-crate`). So crate presence cannot be observed directly.

**The workaround (user intel):** when an oil rig activates, a CH47 (Chinook) flies *to that
rig* to deploy scientists, lingers a few seconds, then leaves the map. The CH47 marker IS
delivered over Rust+ (it is one of the core-3 markers 2a already polls). So a CH47 entering a
rig's radius is a reliable, observable activation signal.

The rig event is determined **solely by the observed CH47 pattern** — a CH47 marker entering a
rig's radius. We do **not** predict a CH47's destination from its spawn position (several rigs
can share a map side; the destination is unknowable until the CH47 actually arrives).

## 2. State model

Per `(guild, server, rig)` the bot runs a timed three-state machine with one external trigger
(the CH47-at-rig event) and two configurable timers.

```text
Online  = crate present, ARMED (resting state; also the assumed state on connect)
   │  CH47 detected at rig  ◄── the only observable trigger
   ▼
Active  = COMBAT phase — scientists deployed, crate unlocking.
   │     The crate becomes lootable at the END of this phase.
   │     (RigActiveWindow, default 15m)
   ▼
Offline = crate now lootable → looted ~instantly in practice → rig dormant.
   │     (RigOfflineWindow, default 15m)
   ▼
Online  = crate respawns → armed again → waits for the next CH47.   (loops)
```

- **Online** — crate present but not lootable; the armed/resting state. **Assumed on connect**
  (we cannot know the true cycle position, and Online is the common resting state). There is no
  separate "Unknown" state.
- **Active** — the combat phase triggered by the CH47 visit. The crate is *unlocking*, not yet
  lootable; it becomes lootable only at the end. Lasts `RigActiveWindow` (15m default), then →
  Offline.
- **Offline** — crate became lootable and (in practice) is looted almost immediately; the rig is
  dormant. Lasts `RigOfflineWindow` (15m default), then → Online.

Transitions (each fires a `RigStateChangedEvent` → `#events` embed + in-game broadcast):

- `Online → Active`: on the CH47-at-rig signal → event `Kind = Activated`.
- `Active → Offline`: timer, after `RigActiveWindow` → event `Kind = CrateLootable`
  (the crate became lootable at the end of the combat phase).
- `Offline → Online`: timer, after `RigOfflineWindow` → event `Kind = Respawned`
  (crate respawned / rig armed again).
- Defensive: a CH47-at-rig signal in **any** state resets that rig to Active (a real CH47 visit is
  ground truth and overrides the timed guess); this also re-fires the `Activated` alert.

State is **in-memory** (consistent with all of 2a's `IEventState`), per `(guild, server, rig)`,
seeded **Online** on first sight, and **cleared on disconnect** (the existing
`ConnectionStatusChangedEvent` handler in `EventsHostedService`).

Two rigs are tracked independently: **small** (`oilrig_1`) and **large** (`large_oil_rig`).

## 3. Detection mechanics

### Rig locations

`RustPlus.GetMapAsync().Monuments` → `List<ServerMapMonument>`. Verified by reflection against
`RustPlusApi 2.0.0-beta.1`: `ServerMapMonument.Name` is populated directly from the protobuf
`AppMap.Monument.Token` (NOT a localized display name), with `Nullable<float> X, Y`. The rig
tokens are:

- small oil rig → `oilrig_1`
- large oil rig → `large_oil_rig`

Monuments are fetched **once on connect** (the same place 2a fetches `dims` in
`RunConnectedAsync`), filtered to the two rig tokens, and the resulting rig positions are passed
into the marker poll for the connected window. If a rig token is absent (map without that rig),
that rig is simply not tracked.

### Activation = CH47-in-rig-radius (only)

On each marker poll, for every live CH47 marker, compute the distance to each tracked rig
monument. If a CH47 is within `RigRadius` (default ~150m, ≈ one grid) of a rig **and that rig is
not already Active**, publish a `RigStateChangedEvent` (`Kind = Activated`) for that rig. Per-rig
debounce: while a CH47 stays continuously in radius, the event fires once, not once per poll.

No spawn-side prediction, no inference fallback — only the observed radius crossing fires the
event.

### Chinook generic event is unchanged

A CH47 spawns at the map edge; its destination is unknowable at spawn, so the existing 2a
**"Chinook spawned"** generic event still fires immediately on CH47 marker add. The rig
activation fires **independently and later**, if/when that same CH47 reaches a rig radius. Both
events are legitimate; we do not suppress the generic Chinook alert. (This is the user's explicit
decision: "we can't know if a ch47 is going to a rig on spawn so we broadcast the spawn event as
well as the oilrig if needed".)

### Poll cadence (adaptive)

The marker poll is one `GetMapMarkers` call per cycle. Cadence:

- **Base interval → 5s** (`MarkerPollInterval` default lowered from 10s to 5s).
- **Adaptive tighten:** while ≥1 CH47 marker is live on the map, the poll runs at
  `MarkerPollFastInterval` (default ~2s) so the few-second at-rig sample is not missed between
  polls. Reverts to the base interval when no CH47 marker is present.

The cadence decision lives in `PollMarkersAsync`: after each poll it picks its next `Task.Delay`
based on whether the current snapshot contains a CH47 marker.

**Known limitation (documented, not blocked):** even at ~2s, a CH47 that is at the rig for only a
moment between two fast polls could in principle be missed. The adaptive tightening minimizes but
does not fully eliminate this; the state machine's timed cycle and the assume-Online default keep
the report sane if a single activation is missed (the next one re-syncs it).

## 4. Surfaces

All four were requested.

1. **`#events` alerts at all THREE rig boundaries.** A localized embed posted to the per-server
   `#events` channel (same channel/style as cargo/heli/chinook) at each rig phase boundary:
   - `Activated` → "🛢️ Small Oil Rig activated — combat phase, crate lootable in ~15m"
   - `CrateLootable` → "🛢️ Small Oil Rig — crate is now LOOTABLE"
   - `Respawned` → "🛢️ Small Oil Rig — crate respawned (armed)"

   EN/FR. The `Activated` alert fires from the CH47 detection; the other two from the timer tick.

2. **In-game team-chat broadcast — generalized to ALL events, 1:1 with `#events`.** Per the
   user's decision, every live event now posts both to `#events` AND to in-game team chat, with
   the in-game feed mirroring `#events` exactly — **every transition**, including the "left"
   transitions (cargo entered AND left, heli entered AND left, chinook spawned, and all three rig
   boundaries). No arrivals-only filtering. `EventRelay` gains an `ITeamChatSender` dependency and
   sends one localized line per event in addition to the Discord embed. (This widens existing 2a
   cargo/heli/chinook behavior, which was Discord-only.)

3. **`!small` / `!large` in-game commands.** Report the current rig state + time remaining in the
   current phase (`DurationFormat.Compact`), e.g.:
   - Online → "Small Oil Rig: crate ready, waiting for activation."
   - Active → "Small Oil Rig: combat phase — crate lootable in ~8m."
   - Offline → "Small Oil Rig: looted / dormant — respawns in ~6m."

4. **`/small` / `/large` slash commands.** The 3c-ii pattern: ephemeral, server-targeted
   (optional+defaulted when one server, disambiguated when many), mirroring the in-game replies.

## 5. Architecture

Follows the exact 2a / 3b / 3c-ii patterns. No new project; no new entities, migrations, or
persistence (all rig state is in-memory like the rest of 2a).

### Abstractions

- `MonumentSnapshot(string Token, float X, float Y)` — new DTO in
  `RustPlusBot.Abstractions/Connections` (namespace `RustPlusBot.Features.Connections.Listening`,
  matching the established 2a/2a convention where these snapshots live in Abstractions but keep
  the Connections.Listening namespace).
- `RigKind` enum — `Small`, `Large`.
- `RigEventKind` enum — `Activated` (Online→Active, the CH47 visit), `CrateLootable`
  (Active→Offline boundary — the crate just became lootable), `Respawned` (Offline→Online
  boundary — crate armed again).
- `RigStateChangedEvent(ulong GuildId, Guid ServerId, RigKind Rig, RigEventKind Kind, float X, float Y, MapDimensions? Dimensions)`
  in `RustPlusBot.Abstractions/Events` (namespace `…Abstractions.Events`). One event type carries
  all three boundary moments; `Kind` selects the user-facing message. `X`/`Y`/`Dimensions` are the
  rig's monument position (for the grid reference), known for all three since the rig is tracked.
  - The CH47-detection path in the poll loop publishes only `Kind = Activated`; the two timed
    boundaries are published by the background tick (below).

### Features.Connections

- `IRustServerConnection.GetMonumentsAsync(TimeSpan timeout, CancellationToken)` →
  `IReadOnlyList<MonumentSnapshot>` (throws on failure, like `GetMapMarkersAsync`). Implemented on
  the **untested integration shim** `RustPlusSocketSource` (maps `GetMapAsync().Monuments`, skips
  monuments with null X/Y) and on `FakeRustSocketSource` (test double).
- `ConnectionOptions` gains:
  - `MarkerPollFastInterval` (default `TimeSpan.FromSeconds(2)`, validated `> Zero`)
  - `RigRadius` (default `150f`, validated `> 0`)
  - `RigActiveWindow` (default `TimeSpan.FromMinutes(15)`, validated `> Zero`)
  - `RigOfflineWindow` (default `TimeSpan.FromMinutes(15)`, validated `> Zero`)
  - `RigTickInterval` (default `TimeSpan.FromSeconds(30)`, validated `> Zero`) — the background
    rig-timer tick cadence (used by `EventsHostedService`, read there, not in the poll loop)
  - and `MarkerPollInterval` default lowered to `TimeSpan.FromSeconds(5)`.
- `RunConnectedAsync` fetches monuments once on connect (next to `dims`), filters to rig tokens,
  passes rig positions into `PollMarkersAsync`.
- `PollMarkersAsync`:
  - keeps its existing add/removed diff + `MapMarkersChangedEvent` publish (unchanged),
  - additionally, on each poll, checks live CH47 markers against rig positions and publishes a
    `RigStateChangedEvent` with `Kind = Activated` per newly-in-radius rig (per-rig debounce so it
    fires once per visit; the debounce can be a small in-loop set of rigs currently "seen with a
    CH47 in radius", cleared when no CH47 is in that rig's radius),
  - chooses its next delay adaptively (fast interval while a CH47 marker is present, else base).

> Note: the per-rig in-radius debounce in the poll loop is only to avoid spamming the `Activated`
> event for the same continuous visit. The authoritative Active/Offline/Online state — and the two
> timed boundary events (`CrateLootable`, `Respawned`) — live in the `RigStateStore` +
> background tick (below). The poll loop only ever emits `Activated`.

### Features.Events

Mirrors 2a's philosophy: rig state **emerges from the event stream** (no connect-time seed
event). An untracked rig is, by definition, **Online** (the assume-Online default); it enters the
store only when its first `Activated` arrives.

- `RigStatus` enum (`Online`, `Active`, `Offline`) + a `RigState` record carrying status + the
  UTC instant the current phase started (for time-remaining math) + the rig's monument X/Y +
  `MapDimensions?` (carried in on the `Activated` event so the tick can publish later boundary
  events with a grid reference without re-querying).
- `RigStateStore(IClock)` — in-memory, keyed by `(guild, server, RigKind)`. Because the two timed
  boundary moments must be **pushed** as events (not just read), a background tick drives timed
  transitions:
  - public `IRigState` read seam: `Get(guild, server, rig)` → current status + time remaining.
    **An untracked rig returns `Online` with no timer** (the default). On read it also settles any
    *overdue* timed transition for a tracked rig so `!small`/`/small` never report a stale phase
    between ticks (idempotent with the tick — whichever runs first wins; the other no-ops).
  - `Apply(activated)` → set that rig Active, stamp now, store its X/Y + dims (defensive override
    from any state; also the implicit "seed"). Used by the `Activated` bus loop.
  - `Advance(now)` → for every tracked rig whose current window has elapsed, transition to the next
    phase, stamp now, and **yield the boundary just crossed** (`CrateLootable` for Active→Offline,
    `Respawned` for Offline→Online). A rig that lands back on `Online` after `Respawned` is dropped
    from the map (back to the untracked = Online default — keeps the tick set bounded). Used by the
    tick; returns the list of (rig, boundary, x, y, dims) crossings to publish.
  - `Clear(guild, server)` → drop all rigs for that server (on disconnect).
  - Reads `RigActiveWindow` / `RigOfflineWindow` from `IOptions<ConnectionOptions>` (consistent
    with `MarkerPollInterval`; `Features.Events` already references `Features.Connections` —
    verified via csproj refs).
- `EventRelay`:
  - consumes `RigStateChangedEvent` (all three `Kind`s) → for `Activated` calls
    `RigStateStore.Apply`; the timed kinds were already advanced+published by the tick → posts the
    `#events` embed (EN/FR, message selected by `Kind`) AND broadcasts in-game via
    `ITeamChatSender`.
  - **generalized in-game broadcast:** for the existing cargo/heli/chinook events it now also
    sends a localized in-game line via `ITeamChatSender` in addition to the Discord embed.
  - gains an `ITeamChatSender` dependency (already a public singleton seam from 3a, backed by
    `ConnectionSupervisor`).
- `EventLocalizationCatalog` gains EN/FR keys for: the three rig boundary messages × {embed,
  in-game line} × {small, large}, plus the in-game lines for cargo/heli/chinook (the embed text
  already exists; the in-game lines are new short strings).
- `EventsHostedService`:
  - adds a `RigStateChangedEvent` bus loop → `EventRelay` (handles all three `Kind`s).
  - **adds a background rig-timer tick** — a periodic loop on a small fixed interval (e.g. 30s, a
    new validated `RigTickInterval` option; fine-grained enough that a boundary alert is at most
    ~one interval late relative to the 15m windows): each tick calls `RigStateStore.Advance(now)`
    and publishes a `RigStateChangedEvent` (`CrateLootable` / `Respawned`) for each crossing it
    returns, using the stored rig X/Y + dims. The tick is the ONLY publisher of the two timed
    boundary events; the marker poll is the only publisher of `Activated`.
  - `Clear` on disconnect already exists (extend it to also clear rig state via
    `RigStateStore.Clear`).

### Features.Commands (in-game)

- `SmallCommandHandler` / `LargeCommandHandler` (`internal sealed ICommandHandler`) reading the
  `IRigState` seam → localized state + time-remaining reply (`DurationFormat.Compact`).
- Registered `AddScoped` in `AddCommands`; `CommandRegistrationTests` handler count +2 (16→18,
  pin the two new handlers by name).
- EN/FR keys appended to `CommandLocalizationCatalog`.

### Features.Commands (slash) — 3c-ii pattern

- Add `/small` and `/large` to the slash surface (`ServerCommandModule` or a sibling), ephemeral,
  server-targeted via the existing `ServerResolver` + `ServerAutocompleteHandler`.
- They read the same `IRigState` seam directly (no new dispatcher handler; rig state is not behind
  `IRustServerQuery` — it lives in `RigStateStore` in Events). An untracked rig reads as Online, so
  both commands work from the first connect with no activation needed. The slash module depends on
  `IRigState` from `Features.Events` (Commands already references Events from 2a for
  `!cargo`/`!events`, so no new project edge).

### What is NOT changed

- No new entity / migration / persistence (rig state is in-memory).
- No new options project (reuse `ConnectionOptions`).
- No new gateway intents (markers + monuments ride the existing Rust+ socket; in-game broadcast
  uses the existing `ITeamChatSender`, which already requires the team-chat send capability from
  3a).
- The generic Chinook event is untouched.

## 6. Testing

- `RustPlusSocketSource.GetMonumentsAsync` is the one **untested integration shim** (consistent
  with every other RustPlusApi mapping); `FakeRustSocketSource` gets a scriptable monument list.
- `PollMarkersAsync` rig-detection + adaptive cadence: covered via the fake's scriptable
  per-poll markers (the 2a `EnqueueMarkers` mechanism) — assert a `RigStateChangedEvent`
  (`Kind = Activated`) fires when a CH47 enters a rig radius, does NOT fire when outside, debounces
  during a continuous visit, and that the generic Chinook event still fires on spawn.
- `RigStateStore`: pure state-machine + timer tests via a `TestClock` — untracked reads Online,
  `Apply` → Active, `Advance` crosses Active→Offline (yields `CrateLootable`) after `RigActiveWindow`,
  Offline→Online (yields `Respawned`) after `RigOfflineWindow` then drops the rig, defensive
  any-state→Active, overdue-settle-on-read idempotent with `Advance`, time-remaining math, `Clear`.
- `EventsHostedService` tick: with a `TestClock`, an Active rig past its window produces a
  `CrateLootable` publish; an Offline rig past its window produces `Respawned`; nothing for an
  untracked/Online rig.
- `EventRelay`: each rig `Kind` (`Activated`/`CrateLootable`/`Respawned`) posts both the embed and
  the in-game line; cargo/heli/chinook now also post the in-game line.
- `SmallCommandHandler`/`LargeCommandHandler` + the new localizer keys: state→reply mapping EN/FR.
- Slash module stays thin/untested (repo convention); the `ServerResolver` path is already
  covered.
- Full-suite discipline: adding `GetMonumentsAsync` to `IRustServerConnection` and `IRigState`
  reads forces updating `FakeRustSocketSource` and any test doubles, or assemblies silently drop
  tests — run the FULL suite and read per-assembly counts (the recurring 3a/3b lesson).

## 7. Execution gotchas (carry-forward)

- Verify the rig tokens against a live server if possible: `oilrig_1` (small) and `large_oil_rig`
  (large) are confirmed as the protobuf tokens the mapper passes through to `Name`, but token
  spelling should be sanity-checked against a real `GetMapAsync` on first live run; tracking is a
  simple token-equality filter, so a wrong token = that rig silently never tracked.
- `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` is the real format gate —
  run it (via `dotnet tool restore`) before pushing; the pre-push hook enforces it.
- NSubstitute on internal interfaces needs `DynamicProxyGenAssembly2` `InternalsVisibleTo` in the
  project under test (already present in Connections/Workspace/Commands; add to Events if a new
  internal seam is mocked there).
- Strict analyzers: `// TODO` comments are errors (`S1135`); param XML docs required
  (`RCS1141`/`CA…`); named-arg ordering (`RCS1205`).
- `docs/superpowers/` specs & plans are LOCAL-ONLY and must never be committed/pushed.
