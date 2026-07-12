# Smart-Device Refactor (RustPlusApi beta.3) — Design

**Date:** 2026-06-22
**Status:** Approved (brainstorming complete; ready for implementation plan)
**Branch:** `feat/smart-device-refactor` off `develop`

## Summary

A **pure refactor** — no new user-facing behavior — that lands on its own branch and
its own PR before the smart-alarm feature continues. It does two things:

1. **Bumps `RustPlusApi` / `RustPlusApi.Fcm` to `2.0.0-beta.3`** and adapts our code to
   that release's breaking renames.
2. **Reshapes the switch in-game-trigger path into a device-agnostic model**, so the
   paused smart-alarm slice (and later storage monitors) can ride the same mechanism
   without further Connections changes.

**Acceptance bar:** after this branch, Smart Switches behave **exactly** as before from
the user's perspective (same embeds, same ON/OFF/Strobe/Rename, same connect-priming,
same live state updates). The only difference is internal: the in-game device-trigger
event is renamed and made device-agnostic, and the package is on beta.3.

## Why this refactor (context)

The smart-alarm slice (`feat/smart-alarms`, Tasks 1–5 committed, paused) initially
modelled an alarm *fire* as an FCM push routed to a specific alarm. That was the **wrong
model**: the FCM alarm event is a "poke the phone" server-level notification, not a
per-entity signal. The correct model — confirmed with the package maintainer — is that
alarms work **exactly like smart switches**: read state on connect via
`GetAlarmInfoAsync` (which also registers the socket's interest), then react to the
socket's in-game device-trigger broadcast. The discriminant between a switch and an
alarm is the **entity id** (our stores know which ids they manage), **not** an entity
type — because the broadcast carries no type.

`RustPlusApi 2.0.0-beta.3` (PRs HandyS11/RustPlusApi#82, #83, #84) makes this explicit by
renaming the switch-specific surface to device-generic. This branch adopts beta.3 and
restructures our trigger path to match, keeping switches working; the alarm slice then
rebases on this branch and plugs in.

## Confirmed beta.3 API surface (verified against the restored 2.0.0-beta.3 DLLs)

**Core (`RustPlusApi`):**

- Event `OnSmartSwitchTriggered` → **`OnSmartDeviceTriggered`**. The `EntityChanged`
  broadcast (`AppEntityChanged`) carries only `entity_id` + `payload` and **omits** the
  `AppEntityType` discriminator — so a switch and an alarm are indistinguishable at
  broadcast time; the type is only knowable by *querying* the entity.
- Event arg `SmartSwitchEventArg` → **`SmartDeviceEventArg`**. It inherits
  `SmartDeviceInfo`, so it carries **`Id` and `IsActive`** — the new value is available
  directly on the trigger arg (no re-read needed to learn the new state).
- Entity types `SmartSwitchInfo` + `AlarmInfo` (byte-identical `{ bool IsActive }`) →
  merged **`SmartDeviceInfo { bool IsActive }`**. The methods
  `GetSmartSwitchInfoAsync`, `GetAlarmInfoAsync`, `SetSmartSwitchValueAsync`,
  `StrobeSmartSwitchAsync`, `ToggleSmartSwitchAsync` all now return
  `Response<SmartDeviceInfo?>`. Both `GetSmartSwitchInfoAsync` and `GetAlarmInfoAsync`
  still exist (their internal mappers keep distinct `Switch`/`Alarm` type guards);
  either primes the socket's interest in the queried entity.
- Mapper `ToSmartSwitchEvent` → `ToSmartDeviceEvent` (internal to the package; not
  consumed by us).

**FCM (`RustPlusApi.Fcm`):**

- `AlarmEvent` removed → **`AlarmNotification`** (`Title` + `Message`, plus inherited
  `ServerId` + `PersistentId` from the new `NotificationBase`). `OnAlarmTriggered`
  payload is now `EventHandler<AlarmNotification?>`. **Not consumed by this branch** —
  the FCM alarm trigger is the wrong model and is not wired here; the rename only needs
  to not break the build (our `develop` FCM source does not reference the alarm trigger,
  so this is a version-availability concern only).
- `Notification<T>` now derives `NotificationBase` (gains `PersistentId`; `ServerId`
  inherited) — additive for existing consumers (`OnServerPairing`,
  `OnSmartSwitchPairing` keep working unchanged).

## Blast radius (verified)

The breaking renames touch only the **Connections** project, three files:
`Listening/IRustServerConnection.cs`, `Listening/RustPlusSocketSource.cs`,
`Supervisor/ConnectionSupervisor.cs`. Building Connections on beta.3 yields exactly one
hard compile error today (`SmartSwitchEventArg` at `RustPlusSocketSource.cs:574`); the
remaining changes are the seam/event reshape. The **Switches** feature project changes
only where it consumes the supervisor's trigger event. No other project references the
renamed members.

## Architecture — the device-agnostic trigger path

### Connections seam (`IRustServerConnection`, internal)

- **Rename** `event EventHandler<ulong>? SmartSwitchTriggered` →
  **`event EventHandler<SmartDeviceTrigger>? SmartDeviceTriggered`**, where
  `SmartDeviceTrigger` is a small internal carrier record `(ulong EntityId, bool IsActive)`
  in `Features.Connections/Listening/`. Carrying `IsActive` is possible because beta.3's
  `SmartDeviceEventArg` exposes it on the trigger arg.
- **Fold** the switch-priming read into a device-generic
  **`Task<bool?> GetSmartDeviceInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken)`**
  (replaces `GetSmartSwitchInfoAsync` on our seam). It calls the package's
  `GetSmartSwitchInfoAsync` under the hood (querying any entity primes the socket's
  interest and returns `IsActive`), serving both switch and — in the next branch — alarm
  priming. Returns `null` on failure/timeout.
- **Keep** the switch-only control methods under their switch names —
  `SetSmartSwitchValueAsync`, `StrobeSmartSwitchAsync` — they are genuinely
  switch-specific operations (alarms have no set/toggle).

### `RustPlusSocketSource` (untested integration shim)

- Subscribe `_rustPlus.OnSmartDeviceTriggered` (was `OnSmartSwitchTriggered`); the
  handler reads `SmartDeviceEventArg.Id` + `.IsActive` and raises our
  `SmartDeviceTriggered(new SmartDeviceTrigger(id, isActive))`. Keep subscribe/unsubscribe
  symmetric.
- Retype the info/set/strobe call sites to `Response<SmartDeviceInfo?>`; map
  `.Data?.IsActive` → `bool?` exactly as the code maps `SmartSwitchInfo` today. **No
  RustPlusApi type crosses the shim boundary** — the seam already projects to
  `bool?`/`bool`.

### `ConnectionSupervisor`

- Trigger handler `OnSmartSwitch(ulong entityId)` → **`OnSmartDevice(SmartDeviceTrigger)`**,
  subscribed to `connection.SmartDeviceTriggered`. It publishes the new
  **`SmartDeviceTriggeredEvent(ulong GuildId, Guid ServerId, ulong EntityId, bool IsActive)`**
  (Abstractions) — using the `IsActive` carried on the trigger, **no re-read**.
- `PrimeSwitchesAsync` → **`PrimeDevicesAsync`**: still lists *switches* (via
  `ISwitchStore.ListByServerAsync`) on this branch — alarms aren't managed yet — calls
  `GetSmartDeviceInfoAsync` per entity to prime + read, and publishes
  `SmartDeviceTriggeredEvent`. (The alarm branch adds alarm priming alongside, in the
  same loop or a sibling call.)
- The trigger handler and `PrimeDevicesAsync` stay inside the existing broad-catch
  connected loop (a failure can't crash the loop or block heartbeat).
- The public `IRustServerQuery` switch methods (`GetSmartSwitchStateAsync`,
  `SetSmartSwitchAsync`, `StrobeSmartSwitchAsync`) keep their names/semantics for the
  switch UI; `GetSmartSwitchStateAsync` is backed by the renamed `GetSmartDeviceInfoAsync`.

### Abstractions

- New **`SmartDeviceTriggeredEvent(ulong GuildId, Guid ServerId, ulong EntityId, bool IsActive)`**.
- **`SwitchStateChangedEvent` stays** — it is still published by the switch **UI module**
  on user actions (ON/OFF/Strobe/Rename) to refresh the embed. Clean semantic split:
  `SmartDeviceTriggeredEvent` = in-game-origin signal (from the supervisor);
  `SwitchStateChangedEvent` = our-UI-origin refresh (from the module).

### Switches feature project (surgical)

- `SwitchStateRelay` grows a handler for **`SmartDeviceTriggeredEvent`**: filter to switch
  ids via `ISwitchStore.ExistsAsync` (ignore ids it doesn't manage — this is the
  decoupling that lets the alarm relay add its own consumer with zero Connections
  changes), then converge on the same "re-render this switch's embed + persist
  LastIsActive" logic the `SwitchStateChangedEvent` path already uses.
- `SwitchesHostedService` gains a third bus loop for `SmartDeviceTriggeredEvent`
  (same per-loop broad-catch as the others).
- The switch **module** is unchanged (keeps publishing `SwitchStateChangedEvent`).
- `IRustServerQuery` switch read/control surface unchanged for callers.

## Data flow (after refactor)

```text
in-game switch toggles:
  socket OnSmartDeviceTriggered (RustPlusSocketSource)         // SmartDeviceEventArg{Id, IsActive}
  → IRustServerConnection.SmartDeviceTriggered(SmartDeviceTrigger{EntityId, IsActive})
  → ConnectionSupervisor.OnSmartDevice → publish SmartDeviceTriggeredEvent(guild, server, entityId, isActive)
  → SwitchStateRelay (filter: ISwitchStore.ExistsAsync(entityId)) → re-render switch embed
      (a non-switch entityId is ignored — alarm branch adds its own consumer)

on (re)connect:
  ConnectionSupervisor.PrimeDevicesAsync → for each managed switch:
      GetSmartDeviceInfoAsync(entityId)  // primes socket interest + reads IsActive
      → publish SmartDeviceTriggeredEvent → SwitchStateRelay re-renders

user presses ON/OFF/Strobe/Rename (SwitchComponentModule):  [UNCHANGED]
  → IRustServerQuery.SetSmartSwitchAsync / Strobe / ISwitchStore.RenameAsync
  → publish SwitchStateChangedEvent → SwitchStateRelay re-renders
```

## Error handling

Unchanged in shape (refactor): supervisor trigger-handler + `PrimeDevicesAsync` inside
the existing broad-catch connected loop; the shim maps failures to `null`/`false` and
never throws with a token; the new `SmartDeviceTriggeredEvent` consumer loop in the
switch hosted service gets the same per-loop broad-catch the existing loops have.

## Testing

TDD, repo conventions. Behaviour-preservation is the bar.

- **`SmartDeviceTriggeredEvent`** — Abstractions test (carries guild/server/entityId/isActive).
- **`ConnectionSupervisor`** — retarget the existing switch-trigger + priming tests to
  assert a **`SmartDeviceTriggeredEvent`** is published (not `SwitchStateChangedEvent`).
  The connection test double (`FakeRustSocketSource` / fake connection) must raise the
  renamed `SmartDeviceTriggered` event with the new `SmartDeviceTrigger` arg — **the
  fakes-must-implement-renamed-members gate** (a missed rename silently drops a whole
  assembly's tests; run the full suite + read per-assembly counts).
- **`SwitchStateRelay`** — new test for the `SmartDeviceTriggeredEvent` path (switch id →
  re-render; non-switch id → ignored via `ExistsAsync`); existing `SwitchStateChangedEvent`
  - disconnect tests stay green.
- **`RustPlusSocketSource`** — untested shim by design; the rename retype is covered by
  the strict build.
- **No new migration, no entity/schema change, no EF drift.**

**Gates:** `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` (0/0);
full suite `-maxcpucount:1` with per-assembly counts (no assembly dropped);
`dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`; no EF drift.

## Out of scope (this branch)

- Any alarm behavior (pairing→embed→toggles→fire). That is the rebased
  `feat/smart-alarms` slice, which plugs into `SmartDeviceTriggeredEvent` +
  `GetSmartDeviceInfoAsync` after this lands.
- Consuming the FCM `OnAlarmTriggered` / `AlarmNotification` (wrong model; not wired).
- `persistentIds` round-trip / `PersistentIdReceived` (a beta.3 FCM feature we don't
  need yet; not adopted here).
- Renaming the switch UI module's `SwitchStateChangedEvent` publishes (kept by decision).

## Conventions / standing gotchas (carry-forward)

- Solution file is **`RustPlusBot.slnx`**; strict build `-warnaserror -maxcpucount:1` = 0/0.
- Run full builds/tests with `-maxcpucount:1` (parallel undercounts + races `.git/config`).
- `dotnet jb cleanupcode … --profile=ReformatAndReorder` is the real format gate (the
  pre-push hook enforces it).
- The repo's `RustPlusBot.Discord` namespace shadows Discord.Net's `Discord` (irrelevant
  here — Connections doesn't touch Discord types).
- NSubstitute on internal interfaces needs `DynamicProxyGenAssembly2` InternalsVisibleTo
  (already present in Connections from 4a).
- New events live in **Abstractions** (no project refs, no Discord, no FCM).
- `docs/superpowers/` is **gitignored / local-only** — never `git add` the spec/plan.

```
