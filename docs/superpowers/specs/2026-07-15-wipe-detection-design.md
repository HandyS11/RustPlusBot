# Server Wipe Detection — Design

**Date:** 2026-07-15
**Status:** Approved (brainstorm gate passed)
**Branch (planned):** feat/wipe-detection off develop

## Problem

When the Rust server wipes (forced/map wipe), the world is reset: every smart
device (switch, alarm, storage monitor) is destroyed in game and its entity id
becomes permanently invalid. Today the bot is blind to this:

- Device rows and their embeds stay in #switches/#alarms/#storagemonitors
  forever, pointing at entities that no longer exist.
- Nobody is told the server wiped; the team discovers it by joining.
- Stale alarms linger as dead entries (they can never trigger again, but they
  clutter the channel and look "armed").

`ServerInfoSnapshot.WipeTimeUtc` (from `getInfo`) and `WorldSnapshot.Seed` /
`WorldSize` (from `getInfo` via `GetWorldAsync`) already expose the signals —
but nothing persists a baseline to diff against, so a wipe is invisible.

**Goal:** detect the wipe when the server comes back up, announce it in the
per-server **#events** channel (optionally pinging @everyone via a global
setting), and purge all paired devices so no stale state or notifications
survive the wipe.

## Decisions (user-confirmed)

1. **Auto-delete all devices on wipe.** Entity ids are permanently invalid
   after a wipe, so switches/alarms/storage monitors (and generic
   `PairedEntity` rows) are deleted outright — DB rows and Discord embeds.
   Users re-pair on the new map. No confirmation prompt, no "wiped" tombstone
   state.
2. **Announcement goes to the per-server #events channel**, alongside the
   existing cargo/heli/rig event posts.
3. **@everyone ping is a global guild setting** (`PingEveryoneOnWipe`) surfaced
   as a toggle in the **#settings** message, **default OFF**.
4. **Detection is event-driven, not supervisor-inline** (approach B below):
   a new `Features.Wipes` project reacts to the existing
   `ConnectionStatusChangedEvent`; `ConnectionSupervisor` is untouched.

## Detection model

A wipe always implies a server restart, which the bot always observes as
disconnect → reconnect (`ConnectionStatusChangedEvent` with
`IsConnected && !WasConnected`). Checking on that transition is sufficient —
no polling needed. Because the baseline is persisted, a wipe that happens
while the **bot** is down is still caught on the next connect.

Approaches considered:

- **A — inline in `ConnectionSupervisor`:** rejected; fattens an already-large
  class and couples it to wipe semantics.
- **B — event-driven feature (chosen):** mirrors how Alarms/Switches/Events
  already consume connection events; fully testable in isolation.
- **C — periodic poll loop:** rejected; wipes cannot happen without a restart,
  so connect-time checks cover every case at zero steady-state cost.

### Baseline persistence

Three nullable columns added to `RustServer` (`Domain/Servers/RustServer.cs`):

| Column            | Type              | Source                             |
| ----------------- | ----------------- | ---------------------------------- |
| `LastWipeTimeUtc` | `DateTimeOffset?` | `ServerInfoSnapshot.WipeTimeUtc`   |
| `LastMapSeed`     | `uint?`           | `WorldSnapshot.Seed`               |
| `LastMapSize`     | `uint?`           | `WorldSnapshot.WorldSize`          |

Plus `GuildSettings.PingEveryoneOnWipe` (`bool`, default `false`). One EF
migration (`WipeDetection`) covers all four.

### Diff rules (WipeDetector)

On each transition to connected, query `IRustServerQuery.GetServerInfoAsync`
and `GetWorldAsync` (either returning null — socket dropped mid-check — aborts
silently; the next reconnect retries):

1. **Empty baseline** (all three columns null — first connect ever, or first
   connect after this feature deploys): store the observed values silently.
   No event. This prevents a false wipe announcement on upgrade.
2. **Wiped** when any of:
   - `newWipeTime > LastWipeTimeUtc + 60s` (tolerance absorbs clock jitter),
   - `Seed != LastMapSeed`,
   - `WorldSize != LastMapSize`.
   Null observed values never match a rule (skipped, not treated as change).
3. On wipe: persist the new baseline **first**, then publish
   `ServerWipedEvent` (consistent with persist-then-publish elsewhere; the
   in-process bus makes the crash window negligible, and purge is idempotent).
4. Otherwise: no-op (plain reconnect).

## Architecture

### New event (Abstractions/Events)

```csharp
public sealed record ServerWipedEvent(
    ulong GuildId,
    Guid ServerId,
    DateTimeOffset? PreviousWipeTimeUtc,
    DateTimeOffset? NewWipeTimeUtc,
    uint Seed,
    uint WorldSize);
```

### New project: `RustPlusBot.Features.Wipes` (+ mirrored test project)

- `Hosting/WipesHostedService` — subscribes to `ConnectionStatusChangedEvent`
  (connected transitions → detector) and to `ServerWipedEvent` (→ announcer).
- `Detection/WipeDetector` (`IWipeDetector`) — the diff rules above.
- `Posting/DiscordWipeChannelPoster` (`IWipeChannelPoster`) — posts to
  #events via the existing `IEventChannelLocator` (Features.Workspace) and
  `DiscordChannelMessenger` pattern; reads `PingEveryoneOnWipe` to decide
  whether the message content carries `@everyone`.
- `Rendering/WipeEmbedRenderer` — the announcement embed: title
  ("🧹 Server wiped"), wiped-at as a Discord relative timestamp, new map size
  and seed fields, and a line stating all paired devices were removed and must
  be re-paired in game. Localized (en/fr resx keys under `wipe.*`).

### Persistence

- `Persistence/Wipes/WipeBaselineStore` (`IWipeBaselineStore`) — reads/updates
  the three `RustServer` baseline columns (single-purpose store, keeps
  `ServerService` focused).
- `PairedEntity` store gains `RemoveByServerAsync(guildId, serverId)`.
- Device stores already expose `ListByServerAsync` + `RemoveAsync` — the wipe
  purge is their first caller outside pairing flows.
- `GuildSettings` store gains the `PingEveryoneOnWipe` accessor/mutator beside
  the existing culture handling.

### Purge fan-out (modular ownership)

Each feature cleans up its own state by subscribing to `ServerWipedEvent` in
its existing hosted service:

- **Features.Alarms / Switches / StorageMonitors** — list rows for the wiped
  server; for each, delete the Discord embed
  (`IWorkspaceGateway.DeleteMessageAsync` via the feature's channel locator,
  missing-message tolerant) then `RemoveAsync` the row. Deleting rows is also
  the stale-notification guarantee: a late `SmartDeviceTriggeredEvent` for a
  dead entity finds no row and is dropped (existing relay behavior — verify
  and cover with a test).
- **Features.Pairing** — `RemoveByServerAsync` on `PairedEntity` rows.
- **Features.Map / Events** — nothing: RustMaps generation keys on
  `(size, seed)`, so the new seed already produces a fresh map; in-memory
  event/rig state is already cleared on disconnect.

Purge handlers are idempotent (re-running on an already-purged server is a
no-op), so a duplicate `ServerWipedEvent` is harmless.

### Settings toggle (Features.Workspace)

- `SettingsMessageRenderer` adds a toggle button
  (`workspace:settings:wipeping`) under the language selector, label showing
  the current state (e.g. "🔔 Ping @everyone on wipe: Off").
- The settings component module (the one handling `LanguageSelectId`) gains a
  handler that flips `GuildSettings.PingEveryoneOnWipe` and re-renders the
  message.

### Ordering note

`ConnectionSupervisor.PrimeDevicesAsync` may prime dead entity ids on the
first post-wipe connect before the purge lands — harmless: dead entities never
broadcast, and priming publishes only no-ping `SmartDeviceStateObservedEvent`s
which drop once the rows are gone.

## Edge cases

- **Bot offline during the wipe:** baseline is persisted → caught on next
  connect.
- **Existing deployments:** first post-deploy connect backfills the baseline
  silently (rule 1).
- **Reconnect without wipe** (network blip, bot restart): all values match →
  no-op.
- **Server rotates IP/endpoint for the new wipe:** out of scope — that arrives
  as a new server pairing, already handled by the pairing confirmation flow.
- **Wipe announcement channel missing** (user deleted #events): poster logs
  and skips, same tolerance as existing event posting.

## Testing

- `Features.Wipes.Tests` — detector: empty-baseline backfill, wipe-time
  advance beyond/within tolerance, seed change, size change, null snapshots
  abort, persist-then-publish order; renderer output; poster ping on/off from
  the guild setting.
- Device feature test projects — purge removes rows + messages, idempotency,
  late trigger for a purged entity is dropped.
- Workspace tests — settings toggle round-trip and renderer state.

## Non-goals

- Predicting or scheduling upcoming wipes (no forced-wipe calendar).
- Automatic device re-pairing after a wipe.
- Per-server (rather than per-guild) ping configuration.
- Blueprint-wipe detection distinct from map wipe (a BP-only wipe without a
  map change and without `WipeTime` moving is indistinguishable and ignored).
