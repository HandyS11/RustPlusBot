# Subsystem 4b — Smart Alarms (v2, socket-trigger model) — Design

**Date:** 2026-06-22
**Status:** Approved (re-brainstormed after the smart-device refactor; ready for implementation plan)
**Branch:** `feat/smart-alarms` (rebased onto `develop` post-refactor PR #22)
**Supersedes:** `2026-06-22-rustplusbot-4b-smart-alarms-design.md` (the FCM-trigger model — now obsolete)

## Why a v2

The original 4b design modelled an alarm *fire* as an FCM push (`OnAlarmTriggered`) routed
to a specific alarm. That was the **wrong model** — verified by decompiling the package:
the FCM alarm event is a server-level "poke the phone" notification carrying only
Title/Message and **no entity/server attribution usable per-alarm**. The correct model
(confirmed with the package maintainer, and enabled by the **smart-device refactor / PR #22**
now on `develop`): **alarms work exactly like smart switches** — primed on connect via the
socket, reacting to the in-game `OnSmartDeviceTriggered` broadcast, with the **entity id** as
the switch-vs-alarm discriminant (the broadcast carries no type).

The refactor landed the generic `SmartDeviceTriggeredEvent(guild, server, entityId, isActive)`
and the `GetSmartDeviceInfoAsync` prime, designed so this slice plugs in with **one small
additive Connections edit** (alarm priming) and otherwise rides the existing seam.

## What carries over from the original (unchanged)

- **Scope:** Smart Alarms only (storage monitors → 4c, switch groups → later).
- **Onboarding:** pair an alarm in-game → FCM `OnSmartAlarmPairing` (typed, per-kind, id-only)
  → user-validated "Add it?" prompt in a per-server `#alarms` channel (NOT auto-registered) →
  on Accept, a persistent per-alarm embed. Default name `Alarm <id>` (the typed FCM event
  carries no name); renamable.
- **Pairing routing:** the entity-pairing path distinguishes switch vs. alarm by
  `PairedEntityKind` (Approach A) and publishes `AlarmPairedEvent` for alarm pairings.
- **UI:** per-alarm embed with **@everyone toggle**, **relay-to-team-chat toggle**, **Rename**.
- **Opt-in / any-member:** alarms are validated, not auto-registered; any guild member operates
  them (role-gating stays subsystem 9).
- **Unreachable handling:** inline on the embed (server not-Connected → ⚠️, toggles disabled),
  no separate channel.
- **Workspace:** per-server `#alarms` `ChannelSpec` (Interactive) + EN/FR `channel.alarms.name`
  (alarms/alarmes) + `IAlarmChannelLocator`.
- **Shared extraction:** the shared `ILocalizer` + `IChannelEmbedPoster` in `RustPlusBot.Discord`
  (the pre-flight duplication decision) still applies — alarms consume them; switches not
  retro-migrated this slice.
- **Already built & kept (the rebased branch has these 3 commits):** the `SmartAlarm` entity
  (revised — see below), `IAlarmStore`/`AlarmStore`, and its DI registration.

## What changes vs. the original (the socket-trigger model)

### 1. Trigger source: socket broadcast, not FCM push

- **Drop entirely:** the FCM `OnAlarmTriggered` subscription, the `PairingKind.AlarmTriggered`
  routing, and the `AlarmTriggeredEvent`. These were the FCM-poke model and are obsolete.
- **Alarms consume the refactor's generic `SmartDeviceTriggeredEvent`** (from Abstractions),
  filtered to alarm ids via `IAlarmStore.ExistsAsync` — exactly as `SwitchStateRelay` filters
  to switch ids. A trigger carries `(entityId, isActive)`; no Title/Message exist on the socket.

### 2. Connect-time priming (the one additive Connections edit)

- **`ConnectionSupervisor.PrimeDevicesAsync` also lists alarms** via a scoped `IAlarmStore`
  (alongside the existing switch listing) and primes each via the existing
  `PublishDevicePrimeAsync` (calls `GetSmartDeviceInfoAsync` → registers socket interest →
  publishes `SmartDeviceTriggeredEvent`). Best-effort per alarm inside the existing broad-catch,
  same as switches. This is the **only** Connections change; the seam/event are untouched.
- Connections gains an `IAlarmStore` reference (both stores live in Persistence, already
  referenced) — additive, no new seam.

### 3. Alarm embed = state + last-triggered (not last-fired Title/Message)

- The embed shows: alarm **name**; current **state** (🔔 Armed / 🚨 Active / ⚠️ Unreachable);
  **"last triggered &lt;N&gt; ago"** (relative time from when it last went active, via `IClock`)
  or "never triggered"; the two toggle indicators.
- On a trigger going **active** (`isActive == true`): update `LastIsActive` + `LastTriggeredUtc`,
  re-render the embed, and **per the alarm's flags** @everyone-ping in `#alarms` and/or relay a
  generic `🚨 {name} triggered` line to in-game team chat (via `ITeamChatSender`). A trigger
  going **inactive** updates state + embed but does **not** ping/relay (only the active edge
  notifies).
- Title/Message are gone (the socket doesn't carry them).

### 4. `SmartAlarm` entity revision (amend the committed entity + regenerate the one migration)

The branch is unmerged (no production DB), so the single `SmartAlarms` migration is regenerated
clean rather than adding a second.

- **Drop** `LastTitle`, `LastMessage`.
- **Add** `bool LastIsActive` (current on/off state, like `SmartSwitch.LastIsActive`).
- **Keep** `LastFiredUtc` → **rename to `LastTriggeredUtc`** (`DateTimeOffset?`, when it last went
  active).
- **Unchanged:** `Id`, `GuildId`, `ServerId` (FK→RustServer cascade), `EntityId`, `Name`,
  `MessageId`, `PairedByUserId`, `CreatedUtc`, `PingEveryone`, `RelayToTeamChat`; unique index
  `(GuildId, ServerId, EntityId)`.
- **`IAlarmStore` revision:** drop `RecordFiredAsync(title, message, firedUtc)`; add
  `UpdateStateAsync(guild, server, entityId, bool isActive, DateTimeOffset? triggeredUtc, ct)`
  (sets `LastIsActive`, and `LastTriggeredUtc` when going active). Keep `AddAsync`/`GetAsync`/
  `ListByServerAsync`/`ExistsAsync`/`RenameAsync`/`SetMessageIdAsync`/`SetPingEveryoneAsync`/
  `SetRelayToTeamChatAsync`/`RemoveAsync`. (The committed store's `AddAsync` no longer seeds
  `LastTitle`/etc.)

## Architecture (final shape)

```text
in-game pair alarm:
  FCM OnSmartAlarmPairing (RustPlusFcmPairingSource)         // typed, per-kind, id-only
  → PairingNotification(Kind=Entity, EntityKind=SmartAlarm, EntityId)
  → PairingHandler.HandleEntityAsync: resolve server by FacepunchServerId
      → EntityKind switch: SmartAlarm → publish AlarmPairedEvent(guild, server, entityId)
  → AlarmPairingCoordinator: if not managed → "Add it?" prompt in #alarms
      → Accept → IAlarmStore.AddAsync (default name) → replace prompt with the alarm embed

on (re)connect:
  ConnectionSupervisor.PrimeDevicesAsync → for each managed switch AND each managed alarm:
      GetSmartDeviceInfoAsync(entityId)   // registers socket interest + reads IsActive
      → publish SmartDeviceTriggeredEvent(guild, server, entityId, isActive)

in-game alarm triggers:
  socket OnSmartDeviceTriggered(SmartDeviceEventArg{Id, IsActive})
  → supervisor → SmartDeviceTriggeredEvent(guild, server, entityId, isActive)
  → AlarmStateRelay (filter: IAlarmStore.ExistsAsync(entityId)):
      → IAlarmStore.UpdateStateAsync(isActive, triggeredUtc = isActive ? now : keep)
      → re-render the #alarms embed
      → if isActive && PingEveryone → @everyone ping in #alarms
      → if isActive && RelayToTeamChat → ITeamChatSender "🚨 {name} triggered"
  (the SwitchStateRelay ignores this id via its own ExistsAsync; the AlarmStateRelay ignores
   switch ids via its ExistsAsync — clean mutual filtering on the one shared event)

toggle @everyone / relay / rename (AlarmComponentModule):
  → IAlarmStore.SetPingEveryoneAsync / SetRelayToTeamChatAsync / RenameAsync → embed refresh

server not-Connected (ConnectionStatusChangedEvent):
  → AlarmStateRelay marks that server's alarm embeds ⚠️ Unreachable (toggles disabled)

server removed (RustServer cascade): SmartAlarm rows deleted by FK cascade.
```

## Components

### Connections (one additive edit)

- **`PrimeDevicesAsync`** also lists + primes alarms via a scoped `IAlarmStore.ListByServerAsync`
  (best-effort per alarm, same broad-catch as switches). Connections csproj gains nothing new
  (Persistence already referenced). No seam/event change.

### Pairing (kind-routing only — the trigger arm is dropped)

- `PairingNotification` keeps `PairedEntityKind EntityKind` (still needed to distinguish switch
  vs. alarm pairings); **does NOT** get `PairingKind.AlarmTriggered`/`Title`/`Message` (those were
  the dropped FCM-trigger pieces — they are not re-added in v2).
- `RustPlusFcmPairingSource` subscribes **only** `OnSmartAlarmPairing` (NOT `OnAlarmTriggered`).
- `PairingHandler.HandleEntityAsync` 2-arm kind switch: SmartSwitch→`SwitchPairedEvent`,
  SmartAlarm→`AlarmPairedEvent`. No `HandleAlarmTriggerAsync`.

### Abstractions

- **`AlarmPairedEvent(ulong GuildId, Guid ServerId, ulong EntityId)`** (kept). **No
  `AlarmTriggeredEvent`** (the slice consumes the refactor's `SmartDeviceTriggeredEvent`).

### Domain / Persistence (revise the committed entity/store + regen migration)

- `SmartAlarm` revised as in change #4; `IAlarmStore`/`AlarmStore` revised (drop RecordFired, add
  `UpdateStateAsync`); regenerate the single `SmartAlarms` migration.

### Features.Alarms (new project, mirrors Features.Switches)

- **`AlarmPairingCoordinator`** — `AlarmPairedEvent` → "Add it?" prompt → Accept persists +
  renders embed. (As original.)
- **`AlarmEmbedRenderer`** (pure) — name; 🔔 Armed / 🚨 Active / ⚠️ Unreachable; "last triggered
  N ago" / "never triggered"; @everyone + relay toggle buttons (disabled when unreachable);
  Rename. EN/FR via shared `ILocalizer` over `AlarmLocalizationCatalog`. `RenderPrompt` for the
  Accept/Dismiss prompt.
- **`IAlarmRefresher`/`AlarmRefresher`** — load alarm + culture + channel, render, post via shared
  `IChannelEmbedPoster`; reused by the relay, coordinator-accept, and the module (DRY).
- **`AlarmStateRelay`** — consumes `SmartDeviceTriggeredEvent` (filter `IAlarmStore.ExistsAsync` →
  `UpdateStateAsync` → refresh → on active edge ping/relay per flags) and
  `ConnectionStatusChangedEvent` (not-Connected → mark unreachable). Relay-failure swallowed.
- **`AlarmComponentModule`** (thin) — Accept/Dismiss; `alarm-ping`/`alarm-relay` toggles →
  store setter → `AlarmRefresher.RefreshAsync`; `alarm-rename` modal. No `[RequireUserPermission]`.
- **`AlarmsHostedService`** — bus loops: `AlarmPairedEvent` → coordinator;
  `SmartDeviceTriggeredEvent` + `ConnectionStatusChangedEvent` → relay.
- **`AlarmLocalizationCatalog`** (catalog dict only) + `AddAlarms` DI (binds `ILocalizer` to the
  alarm catalog, registers renderer/refresher/coordinator/relay/hosted-service +
  `InteractionModuleAssembly`).

## Error handling

- FCM pairing dispatch inside the existing broad-catch fire-and-forget bridge.
- Alarm priming best-effort per alarm inside the supervisor's connected-loop broad-catch (can't
  crash the loop or block heartbeat) — the marker/team-poll lesson.
- Team-chat relay failure → swallow + log; never blocks the embed update or the ping.
- Accept race → `ExistsAsync` + unique index.
- Deleted embed message → shared poster re-posts + caller refreshes `MessageId`.
- No token in any exception/log.

## Testing

TDD, repo conventions.

- `AlarmEmbedRenderer` (armed/active/unreachable; never-triggered vs. last-triggered; toggle
  states; EN/FR).
- `AlarmLocalizationCatalog` key parity.
- `AlarmPairingCoordinator` (prompt + dedupe; Accept persists + renders; race no-op).
- `AlarmStateRelay` (active trigger → UpdateState + refresh + ping-when-on + relay-when-on;
  inactive trigger → UpdateState + refresh, NO ping/relay; non-alarm id → ignored; relay-failure
  swallowed; unreachable-on-disconnect).
- `IAlarmStore` revised (Add/Get/List/Exists/Rename/SetMessageId/toggles/`UpdateStateAsync`/Remove
  - cascade).
- `IAlarmRefresher` (load→render→post; absent/channel-unresolved no-op).
- `ConnectionSupervisor.PrimeDevicesAsync` — alarms primed alongside switches (a managed alarm →
  `SmartDeviceTriggeredEvent` published on connect); the connection test fakes must list alarms.
- Shared `ILocalizer`/`IChannelEmbedPoster` already covered by the refactor.
- **Untested shims (by design):** `RustPlusFcmPairingSource` alarm-pairing wiring;
  `DiscordChannelEmbedPoster` (shared, already in place); thin module/hosted-service.

**Gates:** full suite + per-assembly counts (every fake — incl. the connection fakes that must
list alarms in priming, and `FakeWorkspaceStore` — implements new members or assemblies silently
drop tests); `dotnet build -warnaserror -maxcpucount:1` 0/0; `dotnet jb cleanupcode … --profile=ReformatAndReorder`;
exactly the one regenerated `SmartAlarms` migration (no second migration, no other drift);
`-maxcpucount:1`.

## Conventions / standing gotchas (carry-forward)

- Solution `RustPlusBot.slnx`; entities in Domain; stores `public sealed` in Persistence; events
  in Abstractions (no Discord/FCM); `global::Discord.*` in Discord-touching Alarms files;
  `DynamicProxyGenAssembly2` InternalsVisibleTo for NSubstitute on internals; SQLite can't ORDER BY
  DateTimeOffset (client-side order); EF migrations `--startup-project Persistence`;
  RCS1141 complete XML docs; RCS1217 adjacent-interpolation custom-ids; `docs/superpowers/`
  gitignored — never `git add`.
