# Subsystem 3d — Team Presence Events

Status: design approved 2026-06-18
Builds on: subsystem 3b-ii (team-intel commands via `GetTeamInfoAsync`) and subsystem 2a
(live-events relay pattern: poller → domain event → feature slice → `#events` + in-game chat)
Branch to use: `feat/team-presence-events` off `develop`

## 1. Problem & intel

The bot should fire an alert — in Discord `#events` **and** in-game team chat — whenever a
team member **connects**, **disconnects**, **dies**, or **respawns**, with the map grid square
attached.

**Scope is team members only.** The Rust+ companion API exposes presence/life state for the
paired account's *own in-game team* via `GetTeamInfoAsync` — not for arbitrary players on the
server. "Every player on the server" is not feasible with this connection (would need RCON or a
server plugin) and is out of scope.

**No native push.** `RustPlusApi` 2.0.0-beta.1 has no team-state-changed callback — the only
push event on the socket is `OnTeamChatReceived` (chat). So presence transitions must be
detected by **polling `GetTeamInfoAsync` and diffing successive snapshots**, mirroring how the
marker poller diffs map markers to produce `MapMarkersChangedEvent`.

**Data already available.** `TeamMemberSnapshot` (in
`RustPlusBot.Abstractions/Connections/TeamInfoSnapshot.cs`) already carries everything needed
per member: `SteamId`, `Name`, `X`, `Y`, `IsOnline`, `IsAlive`, `LastSpawnTimeUtc`,
`LastDeathTimeUtc`. No change to the snapshot shape or to `IRustServerQuery` is required.

**Catalog fit.** The feature catalog reserves `/players` and `!players` for the future
Battlemetrics cluster (subsystem 7), so this feature must **not** use those names. It also marks
`!connections` (recent login/logout) and `!deaths` (recent team deaths) as ✅ Adopt under "3b+"
with the note *"Team event history (needs a tracker)."* This subsystem is that tracker's
foundation: the same diff produces the transitions a future `!connections`/`!deaths` history
would record.

**No toggle (this cut).** Player presence events are **always on** while a server is connected,
exactly like the marker poll. No per-server setting, no migration, no `/`- or `!`-command — and
therefore no naming collision with the reserved `/players`.

## 2. Detection — `ConnectionSupervisor`

The supervisor already runs a per-connection marker poll loop (`PollMarkersAsync`) inside the
connected window, calling `GetMapMarkersAsync` and publishing `MapMarkersChangedEvent`. Team
polling is folded into that same connected-window lifecycle (one loop, shared start/join on
disconnect, baseline naturally fresh per connected window).

Each poll also calls `GetTeamInfoAsync`. The supervisor keeps a per-connection baseline
`Dictionary<ulong, TeamMemberSnapshot>` keyed by `SteamId`.

- **Prime silently.** The first team snapshot of a connected window populates the baseline and
  emits **no** events. (The loop — and thus the baseline — is recreated per connected window, so
  reconnects re-prime automatically.)
- **Brand-new member = silent prime.** A member seen for the first time mid-session (just joined
  the team) is added to the baseline silently — it does **not** fire a Connect. Transitions are
  only emitted on a member's *subsequent* polls. This avoids a burst of "connected" lines when
  the bot first observes a full team.
- **Null / failed snapshot = skip.** A null team snapshot (no live socket, API failure) is
  treated as "no data this tick" — keep the existing baseline, emit nothing. Never interpret it
  as "everyone disconnected."

On a member present in both baseline and current snapshot, diff and emit a `PlayerTransition`
for each of:

| Kind | Condition | Location attached |
| --- | --- | --- |
| **Connect** | `IsOnline` false → true | none |
| **Disconnect** | `IsOnline` true → false | none |
| **Death** | `LastDeathTimeUtc` advanced beyond baseline's value | death-location resolution (below) |
| **Respawn** | `LastSpawnTimeUtc` advanced beyond baseline's value | current `X`/`Y` (the live spawn point — always correct) |

Death/respawn are keyed on the **timestamp advancing**, not on the current `IsAlive` flag —
robust against a death-and-respawn that both land between two polls. After diffing, the current
snapshot replaces the baseline.

### Death-location resolution

The Rust+ protocol has **no per-member death coordinate**. `MemberInfo` carries only the member's
**live** `X`/`Y`, which becomes the *spawn point* once they respawn — so reading the current
position on a death can mislabel a bed/beach as the death spot. Two real sources exist, used in
precedence:

1. **Leader → `TeamInfo.DeathNote`.** The protocol exposes a single `DeathNote` — *"the leader's
   death note on the map"* — placed where the leader died (they respawn elsewhere, at a bag/bed).
   It carries only `X`/`Y` (no timestamp or identity), so it is only trusted for the **leader**
   (`member.SteamId == snapshot.LeaderSteamId`) and only when read in the same poll that detected
   the leader's death. This is a true death coordinate.
2. **Non-leader → previous-poll position.** For any other member, use that member's `X`/`Y` from
   the **baseline** snapshot (the previous poll — captured while they were still alive), which
   approximates where they died far better than the post-respawn current position. The baseline
   already holds this; no separate tracking structure is needed.
3. **Neither available** (e.g. a brand-new member who died before a second poll, so no prior
   position) → emit the death with **no location**: `"💀 Name died"`.

To carry source (1), the **`TeamInfoSnapshot` facade is extended** with a nullable
`DeathNote` (a `(float X, float Y)?`), mapped from `TeamInfo.DeathNote.X/Y` in
`RustPlusSocketSource.GetTeamInfoAsync`. This is also the position-tracking groundwork the catalog
flagged as a prerequisite for the deferred `!afk` command (see §2a).

## 2a. AFK detection (sequenced after the core four transitions)

The catalog deferred `!afk` because *"a single snapshot can't detect AFK"* — it needs position
history across polls. The team poll loop now keeps that history, so AFK becomes tractable. AFK is
a **sustained state with hysteresis**, distinct from the four instantaneous edges above.

**Definition.** A member is **AFK** when they are **online AND alive AND have not moved** (their
`X`/`Y` has stayed within `AfkEpsilon` world units) continuously for at least `AfkThreshold`
(configurable, default 5 minutes). Going offline, dying, or moving beyond the epsilon clears AFK.

> Limitation, stated plainly: Rust+ exposes only position, so a member crafting at a bench or
> sleeping in a base reads as "not moving" and will be flagged AFK. This is inherent to the data.

**Tracker state.** Alongside the baseline, the supervisor keeps per member: `StillSinceUtc` (when
the member last started being motionless) and `IsAfk` (whether they have already crossed the
threshold — the hysteresis latch). Each poll, for each online+alive member:

- Moved (squared distance from the baseline position > `AfkEpsilon²` — a true movement radius, so
  diagonal moves aren't undercounted) → reset `StillSinceUtc = now`; if `IsAfk` was true, clear it
  and emit a **ReturnedFromAfk** transition.
- Still, and `now - StillSinceUtc >= AfkThreshold`, and not yet `IsAfk` → set `IsAfk = true` and
  emit a **BecameAfk** transition (location = current `X`/`Y`).
- Offline, dead, **or died this poll** (`LastDeathTimeUtc` advanced — even when a slow poll already
  shows them respawned) → clear `IsAfk` **silently** (no ReturnedFromAfk) and reset the still-since
  clock so AFK is re-earned after the state change. The Disconnect / Death transition already speaks
  for the member; emitting "is back" alongside "disconnected"/"died" in the same poll would
  contradict it.

Per-member `StillSinceUtc`/`IsAfk` state is **pruned** for ids absent from the current snapshot
(member left the team), so the maps don't grow unbounded over a long connected window.

**Two new transition kinds** join the enum: `BecameAfk`, `ReturnedFromAfk`. They flow through the
same `PlayerStateChangedEvent` → `PlayerEventRelay` → `#events` + in-game path as the other four.
BecameAfk carries the current location (grid); ReturnedFromAfk carries none. ReturnedFromAfk is
emitted **only** when a member *moves* out of AFK while still online+alive — never as a side effect
of disconnecting or dying.

**`!afk` query seam.** The point-in-time `!afk` command ("who is AFK right now, and for how
long?") cannot use `IRustServerQuery` (a raw single snapshot). A new read seam
**`IAfkState`** is added in the Connections feature:

```csharp
public interface IAfkState
{
    // null when no live socket; otherwise the currently-AFK members and how long each has been still.
    Task<IReadOnlyList<AfkMember>?> GetAfkMembersAsync(ulong guildId, Guid serverId, CancellationToken ct);
}
public sealed record AfkMember(ulong SteamId, string Name, TimeSpan StillFor);
```

implemented by the supervisor (it owns the tracker). The in-game `!afk` handler injects `IAfkState`,
mirroring how `AliveCommandHandler` injects `IRustServerQuery`. **In-game `!afk` only** this cut —
no `/afk` slash command.

**Config.** Add to `ConnectionOptions`: `AfkThreshold` (`TimeSpan`, default `5m`) and `AfkEpsilon`
(`float`, default a small world-unit tolerance, e.g. `1f`, finalized in the plan).

A poll's transitions (possibly across several members, possibly several kinds per member) are
published as one `PlayerStateChangedEvent`. If a poll yields zero transitions, nothing is
published.

Poll failures follow the existing `PollMarkersAsync` discipline: broad-catch-and-log, keep the
baseline, never crash the loop or the host.

## 3. New slice — `RustPlusBot.Features.Players`

A dedicated feature project mirroring `Features.Events`, keeping presence concerns isolated from
world-event (cargo/heli/rig) concerns.

- **`PlayerTransitionKind`** (enum): `Connect`, `Disconnect`, `Death`, `Respawn`, `BecameAfk`,
  `ReturnedFromAfk` (the last two added in the §2a AFK step).
- **`PlayerTransition`** (record): `Kind`, `SteamId`, `Name`, and a nullable
  `(float X, float Y)? Location`. **The supervisor resolves `Location`**, not the relay:
  - Connect / Disconnect / ReturnedFromAfk → `null` (no location shown).
  - Respawn / BecameAfk → the member's current `X`/`Y` (live position, always correct).
  - Death → resolved per the **Death-location resolution** rules in §2 (leader `DeathNote`,
    else previous-poll position, else `null`).
- **`PlayerStateChangedEvent`** (in `RustPlusBot.Abstractions/Events`): `GuildId`, `ServerId`,
  `MapDimensions`, `IReadOnlyList<PlayerTransition> Transitions`. Published by the supervisor,
  consumed off the in-process event bus.
- **`PlayerEventRelay`**: subscribes to `PlayerStateChangedEvent`; for each transition posts an
  embed to `#events` and a line to in-game team chat. Reuses the existing
  `IEventChannelLocator` (resolves the `#events` channel), `IEventChannelPoster`,
  `ITeamChatSender`, `IWorkspaceStore` (guild culture), and the Events feature's
  `GridReference` formatter to turn a transition's `Location` + `MapDimensions` into a grid
  square (e.g. `G12`). The relay never reads coordinates itself — it renders whatever `Location`
  the supervisor resolved.
- **Two string keys per locatable transition.** Because death location can be absent, death has
  a `player.death` (with grid) **and** a `player.death.unknown` (no grid) key; the relay picks
  by `Location is null`. Connect/disconnect always render the no-location string; respawn always
  has a location.
- **`PlayerLocalizationCatalog`**: EN/FR strings following the `EventLocalizationCatalog`
  convention — an embed key and a `.line` variant per key. Draft strings (final wording during
  implementation), `{0}` = member name, `{1}` = grid square:
  - `player.connect` = "🟢 {0} connected" · `.line` = "{0} connected"
  - `player.disconnect` = "🔴 {0} disconnected" · `.line` = "{0} disconnected"
  - `player.death` = "💀 {0} died at {1}" · `.line` = "{0} died at {1}"
  - `player.death.unknown` = "💀 {0} died" · `.line` = "{0} died"
  - `player.respawn` = "✨ {0} respawned at {1}" · `.line` = "{0} respawned at {1}"
  - `player.afk` = "💤 {0} is AFK ({1})" · `.line` = "{0} is AFK ({1})"
  - `player.afk.back` = "👋 {0} is back" · `.line` = "{0} is back"
  - FR equivalents (connecté / déconnecté / est mort en {1} / est mort / réapparu en {1} /
    est AFK ({1}) / est de retour).
- **`!afk` command** (`Features.Commands`): an `AfkCommandHandler : ICommandHandler` with
  `Name => "afk"`, injecting the new `IAfkState`; lists currently-AFK members with how long each
  has been still (e.g. "Bob (6m), Sue (12m)"), or a "nobody AFK" / "not connected" reply.
  EN/FR strings added to the **command** localizer (not the player catalog).
- **`PlayersHostedService`**: consumes `PlayerStateChangedEvent` from the bus and drives
  `PlayerEventRelay`, mirroring `EventsHostedService` (broad-catch-and-log consumer loop,
  cancel + join on stop).
- **`PlayerEventServiceCollectionExtensions`**: DI registration, wired into
  `Host/Program.cs` alongside the other feature registrations.

`GridReference` currently lives in `Features.Events`. The implementation plan will pick the
cleanest reuse path (reference it across the slice boundary, or lift the small formatter to a
shared location) following the project's existing dependency conventions — without an unrelated
refactor.

## 4. Error handling

- **Team-poll failure / null snapshot** → skip the diff, keep the baseline, emit nothing
  (mirrors `PollMarkersAsync`; never crash the poll loop).
- **Relay consumer fault** → broad-catch-and-log in `PlayersHostedService` (mirrors
  `EventsHostedService`); a faulting relay must not crash the host.
- **No `#events` channel resolved** → still send the in-game line; skip only the Discord post
  (same nullable-channel pattern as `EventRelay`).

## 5. Testing (xUnit, mirroring existing test projects)

- **Diff / classifier:** each transition kind; first-snapshot priming emits nothing;
  brand-new mid-session member primed silently (no Connect); death detected via
  `LastDeathTimeUtc` advance even when `IsAlive` has already flipped back; no events on an
  unchanged snapshot; multiple transitions in one poll (several members and/or several kinds);
  null snapshot keeps baseline and emits nothing.
- **Death-location resolution:** leader death uses `DeathNote.X/Y` when present; non-leader death
  uses the **baseline (previous-poll)** position, not the post-respawn current position; a
  same-poll death+respawn still reports the *pre-death* location, not the spawn point; a death
  with no prior position and no DeathNote yields `Location == null`. Respawn uses current `X`/`Y`.
- **`PlayerEventRelay`:** grid formatting from `Location` + `MapDimensions`; culture selection
  (EN/FR); embed + in-game line content per kind; death with `Location == null` renders the
  `player.death.unknown` string (no grid); `#events` post skipped (in-game still sent) when no
  channel resolves.
- **AFK tracker:** a member still (within `AfkEpsilon`) for `>= AfkThreshold` while online+alive
  emits exactly one `BecameAfk` (not repeated on subsequent still polls — hysteresis latch);
  moving past the epsilon emits `ReturnedFromAfk` and re-arms; going offline or dying while AFK
  emits `ReturnedFromAfk` and clears the track; sub-threshold stillness emits nothing; a member
  who never moves but is offline/dead is never flagged AFK.
- **`IAfkState` / `AfkCommandHandler`:** `GetAfkMembersAsync` returns the currently-AFK members
  with correct `StillFor`; null when no live socket; `!afk` reply lists them / "nobody AFK" /
  "not connected".
- **Localization catalog:** EN and FR keys present for every transition incl. `player.afk` /
  `player.afk.back` (embed + `.line`), mirroring the events catalog test.
- **DI registration:** `PlayerEventRegistrationTests` resolves the relay + hosted service;
  `IAfkState` and the `afk` command handler resolve in their respective registrations.

## 6. Catalog update

In `docs/product/feature-catalog.md`:

- Update roadmap row **3d — Team presence events**: always-on connect / disconnect / death /
  respawn / **AFK** alerts to `#events` + in-game chat (grid square on death/respawn/became-AFK),
  via a `GetTeamInfoAsync` diff in the supervisor poll loop; plus the in-game **`!afk`** command
  on a new `IAfkState` seam.
- Move `!afk` from ⏸ Defer to ✔️ Done under 3d (the position-tracking poller it needed now
  exists).
- Note that 3d lays the tracker groundwork for the ✅-Adopt `!connections` / `!deaths` history
  rows (subsystem "3b+"), which can later record the same transitions this diff produces.

## 7. Out of scope (this cut)

- Per-server **toggle** for presence/AFK events (always-on for now).
- `/afk` slash command (in-game `!afk` only this cut); `/players` stays reserved for subsystem 7
  (Battlemetrics).
- Persisted event **history** and the `!connections` / `!deaths` commands (future; this is their
  foundation).
- Non-team / arbitrary-server-player presence (not exposed by Rust+; see subsystem 7
  Battlemetrics).
