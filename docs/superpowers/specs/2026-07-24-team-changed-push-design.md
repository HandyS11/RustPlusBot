# Push-driven team state via `team_changed` (RustPlusApi beta.6)

**Date:** 2026-07-24
**Status:** Approved design
**Branch:** `fix/consumer-loop-resilience` (or a dedicated follow-up branch)

## 1. Problem & motivation

Team-state detection (connect / disconnect / death / respawn / AFK) is currently
derived by **polling** `GetTeamInfoAsync` inside `PollMarkersAsync`
(`ConnectionSupervisor.cs:761`), on the marker cadence of every 2–5 s
(`MarkerPollInterval` = 5 s, `MarkerPollFastInterval` = 2 s).

Two problems motivate a change:

1. **Efficiency / latency.** A `getTeamInfo` request every 2–5 s per connected
   server is wasteful, and presence/death events lag by up to one poll interval.
2. **Log noise.** RustPlusApi `beta.5` does not dispatch the `team_changed`
   broadcast — its `ParseNotification` falls through to
   `Logger.LogUnknownBroadcast`, producing recurring
   `WRN Unknown broadcast received: RustPlusContracts.AppBroadcast`. `team_changed`
   is the *only* unhandled broadcast field in `beta.5`, so every one of these
   warnings is a dropped team update.

RustPlusApi **beta.6** now parses `team_changed` and raises a new
`OnTeamChanged` event carrying `TeamChangedEventArg { PlayerId, TeamInfo }`,
where `TeamInfo` is produced by the **same `ToTeamInfo()` mapping** the polled
`GetTeamInfoAsync` already uses. Full field parity (members with
`X/Y/IsOnline/IsAlive/LastSpawnTime/LastDeathTime`, `LeaderSteamId`, `DeathNote`).

## 2. Scope

**In scope:** upgrade `beta.5 → beta.6`; move team-state detection from the fast
poll to the `OnTeamChanged` push event; retain a low-frequency team poll purely as
an AFK safety tick.

**Explicitly out of scope:** marker/rig polling. The Rust+ protocol has **no
broadcast** for cargo-ship / patrol-heli / chinook / travelling-vendor markers;
the server only answers `getMapMarkers` on request. `PollMarkersAsync`'s marker
and rig-activation work is unchanged. This is therefore a *partial* migration —
"move team state to push", not "replace the polling system" wholesale.

## 3. Architecture

### 3.1 Connection layer — `IRustServerConnection` / `RustPlusSocketSource`

- Add to the interface:
  `event EventHandler<TeamInfoSnapshot>? TeamChanged;`
- `RustPlusSocketSource`:
  - Subscribe `_rustPlus.OnTeamChanged += OnTeamChanged` in the ctor event block
    (near `RustPlusSocketSource.cs:172`); unsubscribe in `DisposeAsync`
    (near line 768), symmetrically with the existing handlers.
  - Extract the inline `TeamInfo → TeamInfoSnapshot` mapping currently in
    `GetTeamInfoAsync` (lines 339–353) into a private static
    `ToSnapshot(RustPlusApi.Data.TeamInfo)` helper. Both `GetTeamInfoAsync` and
    the new handler reuse it:
    `private void OnTeamChanged(object? s, TeamChangedEventArg e) => TeamChanged?.Invoke(this, ToSnapshot(e.TeamInfo));`
  - Because the event and the poll share one mapping, there is zero behavioural
    drift between pushed and polled team snapshots.
- The test fake `IRustServerConnection` gains the `TeamChanged` event plus a test
  hook to raise it.

### 3.2 Supervisor layer — `ConnectionSupervisor`

- **Push handler.** Wire an `OnTeamChanged` handler in `RunAsync` alongside the
  existing `OnTeamMessage` / `OnClanChanged` wiring (~line 609). On each event it
  calls `tracker.Diff(snapshot, clock.UtcNow, _options.AfkThreshold,
  _options.AfkEpsilon)` and, when transitions are non-empty, fire-and-forget
  publishes `PlayerStateChangedEvent` using the same `_ = PublishAsync(...)`
  error-isolation pattern as the other handlers. Subscribe on setup, unsubscribe
  in the `finally` (lines 655–659 block).
- **Slow team poll (AFK tick + self-heal).** Split the team block (lines 761–768)
  out of `PollMarkersAsync` into a new `PollTeamAsync` loop that runs the *same*
  `Diff` + publish at a new `TeamPollInterval` (default 30 s). Its first iteration
  runs immediately (to prime the baseline promptly), then it waits
  `TeamPollInterval` between iterations. Launched as a fourth `Task.Run` next to
  `markerPoll` / `reachabilityPoll` (line 624) and joined in the same
  `Task.WhenAll` on exit (line 646).
- **`PollMarkersAsync`** loses its `tracker` parameter and the team block; markers
  and rig-activation detection are otherwise untouched.
- **Shared `dims`.** `PlayerStateChangedEvent` needs `MapDimensions?`. `dims` stays
  resolved lazily *off the critical connect path* (preserving the non-blocking
  behaviour the comment at lines 731–735 protects), stored in a per-connected-window
  holder that both the push handler and `PollTeamAsync` read. Events arriving before
  `dims` resolves publish `dims: null`, which `PlayerStateChangedEvent` already
  tolerates (nullable → renders without a grid reference).

### 3.3 Options — `ConnectionOptions`

- Add `public TimeSpan TeamPollInterval { get; set; } = TimeSpan.FromSeconds(30);`

## 4. Concurrency & correctness

- `TeamStateTracker.Diff` is now invoked from two threads: the RustPlusApi dispatch
  thread (push) and the `PollTeamAsync` thread (tick). `TeamStateTracker` is already
  guarded by `_gate` and swaps `_baseline` atomically, so concurrent calls serialize
  and never corrupt shared state. Ordering is NOT guaranteed, though:
  `PollTeamAsync` captures its snapshot before an `await` spanning a network
  round-trip, so a concurrent push can commit a newer baseline first and the poll's
  stale snapshot may then emit a rare, self-healing spurious presence transition
  (corrected on the next tick). This is the accepted cost of the slow-poll
  (Approach B) design; AFK timing is unaffected.
- **No change to the AFK model.** `UpdateAfk` still flags `BecameAfk` when
  `Clock - stillSince >= Threshold`, evaluated whenever `Diff` runs. The slow poll
  exists solely to guarantee `Diff` runs periodically, so "became AFK" cannot stall
  during broadcast silence (a stationary player in a quiet/solo team emits no
  `team_changed`). Worst-case detection latency for BecameAfk is
  `AfkThreshold + TeamPollInterval`; presence/death/respawn/returned-from-AFK remain
  instant via push.
- **Baseline priming.** Whichever of {first broadcast, first `PollTeamAsync`
  iteration} runs first primes the tracker silently (`Diff` primes on first non-null
  call); the other diffs against it. No spurious connect-event flood on connect.

## 5. Testing

- `RustPlusSocketSource`: raising `OnTeamChanged` surfaces a correctly-mapped
  `TeamInfoSnapshot` — member fields, leader death note, offline and dead members.
- `ConnectionSupervisor`: a `TeamChanged` event carrying a moved / disconnected /
  died member publishes the expected `PlayerStateChangedEvent`; and the fast marker
  cadence no longer issues any `getTeamInfo` request.
- AFK-during-silence: with no broadcasts delivered, a stationary online player is
  still flagged `BecameAfk` from the slow poll within
  `AfkThreshold + TeamPollInterval`.
- Regression: existing marker/rig and status-loop tests remain green (the marker
  loop is untouched apart from removing the team block).

## 6. Rollout

- Bump `RustPlusApi` and `RustPlusApi.Fcm` to `2.0.0-beta.6` in
  `Directory.Packages.props`.
- The `Unknown broadcast received` warning disappears once beta.6 dispatches
  `team_changed`; no code change is required to silence it.

## 7. Out-of-scope follow-up (noted, not included here)

The related investigation found that `RustPlusSocketSource.GetMapMarkersAsync`
throws a bare `"GetMapMarkers returned no data."` that discards the server's
`response.Error` code/message, making the `Marker poll ... failed` warning
non-diagnostic. That is a separate, independent fix and is **not** part of this
design.
