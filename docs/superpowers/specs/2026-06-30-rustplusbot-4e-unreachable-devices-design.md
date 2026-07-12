# Subsystem 4e — Per-Device Unreachable Status

**Status:** Approved (brainstorming) · **Date:** 2026-06-30 · **Branch:** `feat/unreachable-devices` off `develop`
**Predecessor:** 4c (PR #27, `d2d57a1`) — Smart Storage Monitors. Subsystem 6 (item DB, 6a–6e) shipped in
between (closed by PR #35, `1672c8e`); this slice resumes and **closes subsystem 4**.

---

## 1. Summary

4e is the final slice of the Smart Devices subsystem. It adds **per-device reachability** to the three
managed device types — **switches, alarms, storage monitors** — surfaced **inline on each device's
embed**, with a **three-way reason**:

- **Removed** — the in-game entity was destroyed (`RustPlusErrorCode.NotFound`).
- **NoPrivilege** — the bot's active player lost building privilege / token access (`AccessDenied`).
- **NoResponse** — the device didn't answer in time (timeout / `Unknown` / `ServerError` / other).

Today the codebase has only a **server-level, binary** unreachable signal: when a whole server is not
`Connected`, every device embed is rendered unreachable (`SwitchStateRelay.HandleConnectionStatusAsync`
and the storage equivalent; the alarm renderer takes an `unreachable` bool). There is **no per-device
axis** — a server can be fully connected while one specific switch was destroyed, and nothing reflects
it. Worse, the connect-time **prime path treats a failed device read as `isActive ?? false`**, so a
removed switch/alarm is silently primed as "off".

4e introduces a persisted `DeviceReachability` axis orthogonal to the existing server-disconnect
signal, threaded up from the one socket adapter that can see the protocol error code, and detected from
two sources: **connect-prime** (all three types) and a **new periodic reachability poll** (catches
removal/privilege-loss while the server stays connected). Switches additionally get **instant**
detection on actuation failure, with a reason-specific reply.

This is **Approach A** from brainstorming: reachability as a typed read result + a device-agnostic
`DeviceReachabilityChangedEvent`, mapping centralized in `RustPlusSocketSource`, each feature relay
plugging in symmetrically (mirrors the existing `SmartDeviceTriggeredEvent` relay idiom).

---

## 2. Scope

### In scope (4e)

- New shared vocabulary in `RustPlusBot.Abstractions`: a `DeviceReachability` enum
  (`Reachable | Removed | NoPrivilege | NoResponse`) and a `DeviceReachabilityChangedEvent`.
- A persisted `Reachability` field on all three domain entities (`SmartSwitch`, `SmartAlarm`,
  `SmartStorageMonitor`), with EF config + **one new migration** `..._DeviceReachability` and a
  `SetReachabilityAsync` store method per type.
- Widened connection seam: device **reads** return payload **+ reachability** in one round-trip;
  switch **actuation** returns `DeviceReachability` (`Reachable` = success). The `RustPlusErrorCode →
  DeviceReachability` mapping lives **only** in `RustPlusSocketSource`.
- Detection from two sources, both publishing `DeviceReachabilityChangedEvent` **only on change**:
  connect-prime (all three) and a new `PollReachabilityAsync` background loop (sibling of
  `PollMarkersAsync`).
- A new `ReachabilityPollInterval` option (default **5 min**) on `ConnectionOptions`.
- Per-feature relay handlers that persist + re-render on reachability change (ownership-filtered by
  `store.ExistsAsync`, same as the trigger path).
- Renderers upgraded from binary unreachable → the three-way reason (status line + control gating).
- Switch actuation (`SwitchComponentModule`) reports the specific reason and updates the embed.
- EN/FR resx keys for the reason labels + switch reply variants; full TDD coverage.

### Deferred / out of scope

- **No `#unreachable-devices` channel.** Per catalog C3/C4 ("prefer inline status on the device embed;
  keep a channel only if it earns it"), reachability is inline-only. The aggregate channel is not built.
- **No `UnreachableSince` timestamp.** The embed states the reason; "since when" isn't requested and
  the socket can't reliably date the loss. Trivial to add later if it earns it.
- **No auto-forget / auto-delete of removed devices.** A `Removed` device keeps its embed (controls
  disabled); deliberate removal stays the user's existing rename/dismiss flows. (A "Forget" button is a
  possible future nicety, noted not built.)
- **No alarm/storage actuation.** Those types have no control surface; their reachability comes from
  prime + poll only. Switches keep their actuation-failure fast path.
- **Cameras / Computer Stations** — subsystem 5, unrelated.

### Explicit non-goals

- No new Discord channel, no change to the existing server-disconnect rendering path.
- No leaking of `RustPlusErrorCode` past `RustPlusSocketSource` (every other seam keeps protocol
  details hidden today; this preserves that).

---

## 3. Vocabulary & domain (Abstractions, Domain)

```csharp
// RustPlusBot.Abstractions.Connections
public enum DeviceReachability { Reachable, Removed, NoPrivilege, NoResponse }

// RustPlusBot.Abstractions.Events
public sealed record DeviceReachabilityChangedEvent(
    ulong GuildId, Guid ServerId, ulong EntityId, DeviceReachability Reachability);
```

The event is **device-agnostic** — one type for all three device kinds — because reachability carries
no device-specific payload. Routing to the correct feature is by `store.ExistsAsync` ownership checks in
each relay, exactly as `SmartDeviceTriggeredEvent` is routed today (the switch relay ignores alarm ids,
etc.).

Each of `SmartSwitch`, `SmartAlarm`, `SmartStorageMonitor` gains:

```csharp
public DeviceReachability Reachability { get; set; } = DeviceReachability.Reachable;
```

This is **orthogonal** to the existing server-disconnect signal (`isActive: null` / `contents: null` /
alarm `unreachable` bool). Server-down still renders all embeds unreachable as it does today; the new
per-device `Reachability` field is a second axis that shows when the server is connected but the device
itself isn't.

---

## 4. Persistence (Persistence + migration)

- `SmartSwitchConfiguration`, `SmartAlarmConfiguration`, `SmartStorageMonitorConfiguration` each map
  `Reachability` as a non-null `int` column, default `0` (`Reachable`).
- One new migration `..._DeviceReachability` adds the three columns (default `0` backfills existing
  rows as `Reachable`).
- Each store (`ISwitchStore`/`SwitchStore`, `IAlarmStore`/`AlarmStore`,
  `IStorageMonitorStore`/`StorageMonitorStore`) gains:

  ```csharp
  Task SetReachabilityAsync(ulong guildId, Guid serverId, ulong entityId,
      DeviceReachability reachability, CancellationToken ct);
  ```

  Mirrors the existing `UpdateStateAsync` / `SetMessageIdAsync` shape.

---

## 5. Connection seam (Abstractions, Features.Connections)

### 5.1 Read/actuation result types

New small result records (Abstractions, beside `StorageContentsSnapshot`):

```csharp
public readonly record struct DeviceReading(bool? IsActive, DeviceReachability Reachability);
public readonly record struct StorageReading(StorageContentsSnapshot? Contents, DeviceReachability Reachability);
```

Seam changes (all three layers — `IRustServerConnection`/`RustPlusSocketSource`, `IRustServerQuery`,
`ConnectionSupervisor` — the upper two just forward):

| Method | Today | After |
|---|---|---|
| `GetSmartDeviceInfoAsync` (switch + alarm prime) | `Task<bool?>` | `Task<DeviceReading>` |
| `GetStorageMonitorInfoAsync` / `GetStorageContentsAsync` | `Task<StorageContentsSnapshot?>` | `Task<StorageReading>` |
| `SetSmartSwitchValueAsync` / `SetSmartSwitchAsync` | `Task<bool>` | `Task<DeviceReachability>` |
| `StrobeSmartSwitchAsync` | `Task<bool>` | `Task<DeviceReachability>` |

`GetSmartSwitchStateAsync` (the query-side switch read) stays `Task<bool?>` — its only caller
(`SwitchComponentModule.StrobeAsync`) runs *after* the strobe already resolved reachability, so it needs
only the on/off bit. (As-built clarification: the seam widening for the query side applies to the two
actuation methods only; `GetSmartSwitchStateAsync`/`GetStorageContentsAsync` keep their existing return
types.)

### 5.2 The mapping (only in `RustPlusSocketSource`)

```text
response.IsSuccess                              -> Reachable    (payload = response.Data)
response.Error.Code == NotFound                 -> Removed
response.Error.Code == AccessDenied             -> NoPrivilege
otherwise (Unknown/ServerError/RateLimit/...)   -> NoResponse
OperationCanceledException (timeout, not shutdown) -> NoResponse
no live socket for (guild, server)              -> NoResponse
```

When `Reachable`, the payload (`IsActive` / `Contents`) is populated as today; otherwise it is `null`.
Shutdown-driven `OperationCanceledException` still propagates (unchanged). No `RustPlusErrorCode` value
escapes this file.

### 5.3 Detection — prime

- `PublishDevicePrimeAsync` (switches + alarms) and `PublishStoragePrimeAsync` (storage) now read via
  the widened calls and publish `DeviceReachabilityChangedEvent` with the read reachability
  **unconditionally on connect** (one-shot). On `Reachable` they also publish state as today; on a
  non-`Reachable` reading they publish the reason and **skip the state publish** — a removed device is no
  longer published as `IsActive=false`. The relay is the idempotency point (it persists then re-renders;
  `poster.EnsureAsync` edits in place), so an unchanged `Reachable` prime is a harmless no-op re-render.
  Publishing on connect guarantees a device that recovered while the bot was offline is cleared back to
  `Reachable`.

### 5.4 Detection — periodic poll

New `PollReachabilityAsync(key, connection, ct)`, spawned in the connected window beside the marker poll
(`Task.Run`, linked CTS, joined in the same `finally`). Each cycle:

1. List the server's switches + alarms + storage monitors (scoped stores).
2. Read each device via the widened reads.
3. Diff each entity's reachability against an in-memory `Dictionary<ulong, DeviceReachability>` snapshot
   owned by the loop (the `previous`/diff pattern `PollMarkersAsync` already uses).
4. For each entity whose reachability **changed since the loop's own previous cycle** (including recovery
   → `Reachable`), publish `DeviceReachabilityChangedEvent` and update the snapshot.

The **first cycle seeds the snapshot from its own first read and publishes nothing** (the connect-prime
already announced the initial state; the poll only reports subsequent transitions). The poll's snapshot
is loop-local and independent of the prime path. Cadence: `ReachabilityPollInterval` (default 5 min) —
removal/privilege-loss is rare and each device is a socket round-trip, so the cadence is conservative;
switches additionally get instant detection on actuation. **Reachability is the only signal the poll
emits** — it does not republish state/contents (those stay trigger- and prime-driven), so a steady-state
poll cycle with no changes writes nothing and touches no embed.

`ConnectionOptions` gains:

```csharp
/// <summary>How often to poll managed devices for reachability changes while connected. Default 5m.</summary>
public TimeSpan ReachabilityPollInterval { get; set; } = TimeSpan.FromMinutes(5);
```

---

## 6. Relays, rendering, actuation (Features.Switches/Alarms/StorageMonitors)

### 6.1 Relay handlers

Each feature's hosted service subscribes to `DeviceReachabilityChangedEvent`; the relay handler:

1. `store.ExistsAsync(guild, server, entityId)` — ignore entities this relay doesn't own.
2. `store.SetReachabilityAsync(...)` — persist the new reachability.
3. Re-render the embed with the persisted reason (load the entity, render, `poster.EnsureAsync`).

`SwitchStateRelay`, `StorageMonitorStateRelay` gain a `HandleReachabilityChangedAsync`; alarms route
through `AlarmStateRelay` + `AlarmRefresher` (the refresher already takes the unreachable signal —
upgrade it to carry the reason).

### 6.2 Rendering

Renderers thread the persisted `Reachability` (replacing / extending the binary unreachable arg):

| Reachability | Embed |
|---|---|
| `Reachable` | unchanged — state shown, controls enabled |
| `NoResponse` | ⚠️ "no response"; last-known state greyed; controls **enabled** (likely transient) |
| `NoPrivilege` | ⛔ "no building privilege"; controls **disabled** |
| `Removed` | ❌ "removed in-game"; controls **disabled** |

The existing **server-disconnect** render still takes precedence when there's no socket (whole server
down); the per-device reason shows when the server is `Connected` but the device is not reachable.

### 6.3 Switch actuation feedback

`SwitchComponentModule.SetAsync` / `StrobeAsync`: the `bool ok` becomes a `DeviceReachability`. On a
non-`Reachable` result:

- Ephemeral reply names the reason: "This switch was removed in-game." / "No building privilege." /
  "The switch didn't respond." (EN/FR via `ILocalizer`).
- Publish `DeviceReachabilityChangedEvent` so the embed updates immediately (today it just replies
  "unreachable right now" with no embed change).

On `Reachable`, behaviour is unchanged (publish the state-changed event with the requested value).

---

## 7. i18n (C5)

New EN/FR keys in the shared `Strings.resx` / `Strings.fr.resx`, resolved via the existing `ILocalizer`:

- Three reason labels (status lines) used by all three renderers.
- The three switch actuation reply variants.

Game data (device names) stays as-is; only bot UI text is localized, per charter.

---

## 8. Testing (TDD)

- **Mapping:** `RustPlusErrorCode → DeviceReachability` for every branch, incl. timeout
  (`OperationCanceledException` non-shutdown) and no-live-socket → `NoResponse`; shutdown cancellation
  still propagates.
- **Poll:** diff/publish-on-change — change, recovery → `Reachable`, first-cycle baseline silence, no
  publish when unchanged; lifecycle (cancelled with the connected window).
- **Prime:** removed device publishes `Removed` (not `IsActive=false`); reachable device publishes
  state as before.
- **Relays:** ownership filter (foreign entity ignored), persist via `SetReachabilityAsync`, re-render.
- **Switch module:** reason-specific reply + reachability event on each non-`Reachable` result; normal
  path unchanged.
- **Renderers:** snapshot per reason × per device type (switch/alarm/storage), incl. control gating.
- **Persistence:** migration round-trips; `SetReachabilityAsync` per store.

Follows the established per-feature xUnit layout.

---

## 9. Files touched (orientation, not exhaustive)

- **Abstractions:** `DeviceReachability`, `DeviceReachabilityChangedEvent`, `DeviceReading`,
  `StorageReading`; widened `IRustServerQuery`.
- **Domain:** `SmartSwitch`, `SmartAlarm`, `SmartStorageMonitor` (+`Reachability`).
- **Persistence:** 3 `*Configuration.cs`, 3 stores (+`SetReachabilityAsync`), 1 migration.
- **Features.Connections:** `IRustServerConnection`, `RustPlusSocketSource` (mapping), `ConnectionSupervisor`
  (widened forwards, prime publishes reachability, `PollReachabilityAsync`), `ConnectionOptions`.
- **Features.Switches:** `SwitchStateRelay` (+handler), `SwitchEmbedRenderer`, `SwitchComponentModule`,
  `SwitchesHostedService` (subscription).
- **Features.Alarms:** `AlarmStateRelay`/`AlarmRefresher` (+handler/reason), `AlarmEmbedRenderer`,
  `AlarmsHostedService`.
- **Features.StorageMonitors:** `StorageMonitorStateRelay` (+handler), `StorageMonitorEmbedRenderer`,
  `StorageMonitorsHostedService`.
- **Localization:** `Strings.resx` / `Strings.fr.resx`.

This slice **closes subsystem 4**.
