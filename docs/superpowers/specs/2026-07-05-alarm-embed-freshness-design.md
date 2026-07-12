# Alarm embed freshness — live timestamp, silent state sync, Refresh button

**Date:** 2026-07-05
**Branch:** fix/device-reachability (continues the reachability bugfix work, same branch by user request)
**Status:** Approved

## Problem

The alarm embed's "Last triggered <1m ago" is a string baked in at render time
(`AlarmEmbedRenderer.CompactDuration(clock.UtcNow - t)`). Discord embeds are static, so the
value freezes until the next re-render (next trigger, reconnect, sweep-detected reachability
*change*, or button click). Users see a wrong duration almost immediately. More broadly, an
alarm's Armed/Triggered state can silently drift if a trigger broadcast is missed (e.g. brief
disconnect), and there is no way to force a re-sync from Discord.

## Decisions (user-approved)

1. **Live timestamp + sweep sync** over blind periodic re-renders.
2. **Refresh button on alarm embeds only** (not switches).
3. Out of scope: switch drift sync, storage protection-expiry timestamps (same technique
   applies later), any library (RustPlusApi) changes.

## Design

### 1. Native Discord relative timestamp

`AlarmEmbedRenderer.RenderAlarm` renders the last-triggered line as
`<t:{alarm.LastTriggeredUtc.ToUnixTimeSeconds()}:R>` via the existing
`alarm.embed.lasttriggered` localization argument. Discord clients render "2 minutes ago" and
keep it updating live — no server-side re-render needed for the timer, ever.

- `CompactDuration` and the renderer's `IClock` dependency are removed (the relay keeps its
  own `IClock` for stamping `LastTriggeredUtc` on real triggers).
- `alarm.embed.nevertriggered` unchanged.

### 2. Silent state sync — `SmartDeviceStateObservedEvent`

New event in `RustPlusBot.Abstractions/Events`:
`SmartDeviceStateObservedEvent(ulong GuildId, Guid ServerId, ulong EntityId, bool IsActive)`.

Semantics: "a poll/prime/refresh *observed* this state" — as opposed to
`SmartDeviceTriggeredEvent` ("the device broadcast a state change"). Observed events NEVER
ping @everyone or relay to team chat; only real triggers do.

**Consumer** — `AlarmStateRelay.HandleStateObservedAsync` (new consumer loop in
`AlarmsHostedService`):

- entity not a managed alarm → ignore;
- observed state equals `alarm.LastIsActive` → no-op (no Discord edit in steady state);
- drifted → persist `LastIsActive` via `UpdateStateAsync(..., isActive, lastTriggeredUtc: null)`
  (LastTriggeredUtc untouched — we don't know *when* it changed), then refresh the embed.

**Publishers:**

- **5-minute reachability sweep** (`ConnectionSupervisor.ReadAllReachabilityAsync`): for every
  alarm read that is `Reachable` with a non-null `IsActive`, publish the observed event
  (the sweep already reads the state and currently discards it — zero extra Rust+ calls).
- **Connect prime** (`PublishDevicePrimeAsync`): for `SmartDeviceKind.Alarm`, publish the
  observed event INSTEAD of `SmartDeviceTriggeredEvent`. Fixes a latent wart: a reconnect
  while an alarm is active currently re-pings @everyone. Switch primes keep publishing
  `SmartDeviceTriggeredEvent` (switch embeds rely on it; switches have no ping semantics).

Reachability recovery after a disconnect is unaffected: the prime still always publishes
`DeviceReachabilityChangedEvent`, which persists and re-renders the embed.

### 3. Refresh button (alarms)

Mirror of the storage monitor Refresh button:

- `AlarmComponentIds.RefreshPrefix = "alarm:refresh:"`, tail `{serverId}:{entityId}`.
- `AlarmEmbedRenderer` adds a Refresh button to the row, disabled under the same
  `unreachable || blocked` condition as the other buttons.
- New kind-aware read on `IRustServerQuery`:
  `Task<DeviceReading> GetSmartAlarmReadingAsync(ulong guildId, Guid serverId, ulong entityId, CancellationToken ct)`
  — supervisor implements via `GetSmartDeviceInfoAsync(entityId, SmartDeviceKind.Alarm, ...)`;
  no live socket → `DeviceReading(null, NoResponse)`.
- `AlarmComponentModule.RefreshAsync(tail)`: defer ephemeral → read → publish
  `DeviceReachabilityChangedEvent(reading.Reachability)` and, when reachable with a state,
  `SmartDeviceStateObservedEvent(reading.IsActive)` → followup "Refreshed." /
  "Alarm is unreachable right now." (hardcoded English ephemerals, matching the storage module).
  Persist/render stays in the relay pipelines — the module only reads and publishes.

### 4. Localization

`alarm.button.refresh` added to `Strings.resx` + `Strings.fr.resx` (keeps en/fr parity).

## Testing (TDD, red first)

1. Renderer: last-triggered line contains `<t:{unix}:R>`; Refresh button present and disabled
   when blocked/unreachable (`AlarmEmbedRendererTests`).
2. Relay observed handler: no-op when state unchanged; persists + re-renders on drift; never
   pings/relays even on an observed false→true edge (`AlarmStateRelayTests`).
3. Hosted service: `SmartDeviceStateObservedEvent` reaches the relay (`AlarmsHostedServiceTests`).
4. Sweep: publishes observed events for a reachable alarm (`Connections.Tests`, mirrors
   `StorageSweepTests`).
5. Prime: alarm prime publishes observed (not triggered) — update `AlarmPrimingTests`.
6. Module: refresh handler publishes both events on a reachable read (existing alarm module
   test patterns).

## Out of scope / future

- Switch state drift sync (same observed-event pipeline would extend naturally).
- `<t:...:R>`/`<t:...:f>` for storage protection expiry and other rendered durations.
- RustPlusApi beta.4 items (documented in RustPlusApi/docs/development/beta4-entity-info-fixes.md).
