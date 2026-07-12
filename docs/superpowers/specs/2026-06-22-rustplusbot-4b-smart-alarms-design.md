# Subsystem 4b — Smart Alarms — Design

**Date:** 2026-06-22
**Status:** Approved (brainstorming complete; ready for implementation plan)
**Branch:** `feat/smart-alarms` off `develop`

## Summary

Subsystem 4b is the second slice of subsystem 4 (smart devices), after 4a
(Smart Switches). It builds **Smart Alarms end-to-end** on the **FCM-push** model:
pair an alarm in-game → validate it in Discord → get a per-alarm embed in a
per-server `#alarms` channel. When the alarm fires in-game, an FCM
`OnAlarmTriggered` push (`Title` + `Message`) updates that alarm's embed with the
last-fired data, and — per the alarm's toggles — pings `@everyone` in `#alarms`
and/or relays the message into in-game team chat.

Storage monitors, switch groups, a dedicated unreachable-devices channel,
cameras, custom-message overrides, recycle/upkeep calc, and in-game/slash alarm
command equivalents are explicitly out of scope (later slices / subsystems).

The slice follows the established feature-slice pattern, mirroring `Features.Switches`
closely. Two architectural additions over the switch template:

1. Threading the already-existing `PairedEntityKind` enum through the entity-pairing
   path so a switch pairing and an alarm pairing route to different events (Approach A).
2. **Shared localizer + shared Discord embed poster** (decided during execution
   pre-flight): rather than copy the switch slice's `SwitchLocalizer`/
   `SwitchLocalizationCatalog` shape (the 5th localizer copy) and
   `DiscordSwitchChannelPoster` verbatim, 4b extracts two reusable pieces into the
   **`RustPlusBot.Discord`** project (already referenced by every feature project):
   - a shared **`ILocalizer` + `Localizer`** (the dictionary-catalog + EN-fallback +
     region-normalization logic, identical across the 5 existing copies) — each
     feature supplies only its own `culture → key → value` catalog dictionary and
     consumes the shared localizer; this pays down the standing "consolidate
     localizers someday" debt for the alarm slice.
   - a shared **`IChannelEmbedPoster` + `DiscordChannelEmbedPoster`** (the
     `EnsureAsync` ensure/post/edit-by-message-id shim with the deleted-message
     self-heal) — alarms consume it directly instead of a slice-specific poster.

   **Scope of the dedup (deliberately minimal):** only the localizer and the embed
   poster are shared. The store, coordinator, relay/refresher, component module, and
   hosted service stay slice-specific (still structurally parallel to switches — a
   larger framework extraction was considered and rejected as out-of-scope). The
   **existing switch/event/player slices are NOT retro-migrated** onto the shared
   pieces in 4b (an optional follow-up); only `Features.Alarms` uses them, so the
   change is additive and low-risk.

## Goals

1. Detect a new Smart Alarm pairing via the FCM listener (`OnSmartAlarmPairing`).
2. Require **user validation** ("Add it?") before a detected alarm becomes managed —
   alarms are **not** auto-registered (same opt-in model as switches).
3. Persist accepted alarms so they survive restarts.
4. One embed per alarm in a per-server `#alarms` channel.
5. React to an alarm firing in-game (FCM `OnAlarmTriggered` push): update the
   embed's last-fired title/message/timestamp, and per the alarm's flags ping
   `@everyone` in `#alarms` and/or relay the message into in-game team chat.
6. Controls per embed: **@everyone toggle**, **relay-to-team-chat toggle**, **rename**.
7. Show unreachable/removed alarm servers inline on the embed (⚠️, toggle buttons
   disabled) when the server is not Connected; no separate channel.
8. Any guild member may validate and operate alarms (role-gating stays in subsystem 9).

## Non-Goals (out of scope for 4b)

- **Storage monitors** (FCM `OnStorageMonitorPairing`; socket
  `GetStorageMonitorInfoAsync` / `OnStorageMonitorTriggered`; contents, TC upkeep;
  recycle calc needs the item DB) → **4c** / subsystem 6.
- **Switch groups** (collective on/off over already-managed switches) → a later slice.
- A dedicated **unreachable-devices channel** — inline status only.
- **Camera** entities → subsystem 5.
- **Custom-message override** per alarm — only Rename this cut (keeps the modal
  surface to one field, like switches).
- **Live socket alarm read/control** — `GetAlarmInfoAsync` exists but is not used;
  there is no on/off control and no socket trigger event for alarms. State is
  event-driven from FCM only (no connect-time priming for alarms).
- In-game `!`-command and `/`-slash equivalents for alarms — the Discord embed
  surface is the whole of 4b; commands can be added later.

## Key behavioral difference from switches

Switches are **socket-controlled** (prime on connect, set/toggle/strobe, react to
the socket's `OnSmartSwitchTriggered`). Alarms are **notify-only over FCM push**:

- The meaningful signal is the FCM `OnAlarmTriggered` push, carrying `Title` +
  `Message` (the user-configured strings). There is **no socket trigger event**,
  **no set/toggle**, and **no connect-time priming** for alarms.
- Therefore 4b adds **no socket read/control** and **no supervisor priming**: it
  does not extend `IRustServerConnection` / `IRustServerQuery` / `RustPlusSocketSource`
  and does not touch `ConnectionSupervisor`. It references `Features.Connections`
  for **one reason only** — the existing public `ITeamChatSender` seam (which lives
  in `Features.Connections.Listening`) for the relay-to-team-chat toggle, exactly as
  `Features.Chat` and `Features.Switches` already do. This is a far smaller surface
  than 4a's switch slice, which extended the socket seam end-to-end.
- The embed reflects last-fired data + two toggles, not a live socket value. An
  alarm whose server is not Connected is shown ⚠️ Unreachable with toggle buttons
  disabled (consumes `ConnectionStatusChangedEvent`, same as switches' disconnect
  arm) — but there is no "Connected republishes real state" arm, because there is
  no live state to prime.

## Confirmed RustPlusApi / FCM surface (verified against 2.0.0-beta.2 XML docs)

> **Package version:** 4b stays on **`RustPlusApi` / `RustPlusApi.Fcm` 2.0.0-beta.2**
> (already in `Directory.Packages.props` from 4a). No package bump.

**FCM (`RustPlusApi.Fcm.RustPlusFcm`) events used:**

- **`OnSmartAlarmPairing`** → `EventHandler<Notification<ulong?>>` — the **typed,
  per-kind** pairing event the package fires only for `EntityType == Alarm`, so 4b
  subscribes to exactly the kind it cares about and does **not** branch on entity
  type in the FCM source. `Notification.Data` is the entity id as `ulong?`
  (beta.2 — already the socket's `ulong`); a `null` id → drop. This mirrors how 4a
  uses `OnSmartSwitchPairing`. The typed event carries **no entity name** — the
  prompt/embed default to a generic `Alarm <id>` (renamable), same trade-off 4a
  made for switches.
- **`OnAlarmTriggered`** → `EventHandler<Notification<AlarmEvent>>`, where
  `AlarmEvent { string? Title, string? Message }` — **the typed payload carries only
  Title and Message, NOT an entity id.** The underlying `Notification<T>` still
  carries `Guid ServerId` (Facepunch GUID), `PlayerId`, `PlayerToken`; the raw
  `Body` (reachable as 4a reaches it for endpoint resolution) carries
  `Body.EntityId` (`ulong?` in beta.2) plus `Ip`/`Port`/`Name`.
- `Notification<T> { T? Data, ulong PlayerId, int PlayerToken, Guid ServerId }`.
  `Body { ulong? EntityId, string? EntityName, EntityType? EntityType, string? Ip,
  int Port, string? Name, ulong PlayerId, int PlayerToken, … }`.

**Socket (`RustPlusApi.RustPlus`) — NOT used by 4b:** `GetAlarmInfoAsync` →
`AlarmInfo` exists (read-only state) but is deliberately unused; there is **no**
socket-side alarm trigger event, set, or toggle. (Listed only to document why the
Connections seam is untouched.)

### Alarm-trigger entity-id caveat (VERIFY during execution)

The typed `OnAlarmTriggered` payload (`AlarmEvent`) has only Title/Message, so the
entity id for a fire must come from the **raw `Body.EntityId`**. 4a confirmed
`Body.EntityId` is `ulong?` in beta.2 and **populated for pairings**; whether it is
populated on a **trigger** notification must be **verified against a live alarm
during execution**. Per the "drop unmatched fires" decision, a trigger with no
usable entity id is logged and dropped, so the design degrades safely if the id is
absent. **Contingency (not the primary path):** if triggers reliably lack an entity
id, fall back to server-only attribution (a fire updates the single managed alarm on
that server; ambiguous when a server has multiple alarms — log and drop in that
case). The primary path is `Body.EntityId` match.

## Architecture

Follows the established feature-slice pattern (`Features.Switches`, `Features.Events`,
`Features.Players`): a **new `RustPlusBot.Features.Alarms` project** owns the Discord
surface and orchestration, on top of a **kind-tagged extension** of the existing
**Pairing** entity path, with a new **`SmartAlarm` entity** in Domain + store in
Persistence, and a per-server **`#alarms` channel** in Workspace.

**Approach A — kind-tagged pairing, separate per-kind events** (chosen over a
generic shared `EntityPairedEvent` that would re-open shipped 4a switch code, and
over an Alarms-owned FCM subscription that would duplicate Pairing's server
attribution). Approach A keeps alarms a fully isolated pipeline; the only shared-code
edits are surgical.

### Project dependency arrows (no cycles)

- `Features.Alarms` → Abstractions, Persistence, Domain, Discord, Workspace.
- Plus the existing public **`ITeamChatSender`** seam (for the relay-to-team-chat
  toggle). **No dependency on Connections** (alarms have no live-socket needs).
- Pairing extensions stay within the existing Pairing project graph.
- New events live in **Abstractions** (dependency-free, FCM-free), consumed by
  `Features.Alarms`.

### Data flow

```text
in-game pair alarm
  → FCM OnSmartAlarmPairing (RustPlusFcmPairingSource)        // typed, per-kind, id-only
  → PairingNotification(Kind=Entity, EntityKind=SmartAlarm, EntityId)
  → PairingHandler.HandleEntityAsync: resolve server by FacepunchServerId
      → switch EntityKind: SmartAlarm → publish AlarmPairedEvent(GuildId, ServerId, EntityId)
  → AlarmPairingCoordinator (Features.Alarms): if not already persisted
      → post transient "New alarm detected (Alarm <id>) — Add it? [Accept][Dismiss]" in #alarms
      → user Accept → IAlarmStore.AddAsync (default name "Alarm <id>") → replace prompt with the alarm embed

alarm fires in-game:
  → FCM OnAlarmTriggered (RustPlusFcmPairingSource)           // Notification<AlarmEvent{Title,Message}>
  → resolve server by FacepunchServerId; read entity id from raw Body.EntityId
  → publish AlarmTriggeredEvent(GuildId, ServerId, EntityId, Title, Message)
  → AlarmFireRelay (Features.Alarms): resolve persisted alarm by (guild,server,entityId)
      → none → log + drop (unmatched)
      → IAlarmStore.RecordFiredAsync(title, message, firedUtc) → re-render embed
      → if PingEveryone → @everyone notification in #alarms
      → if RelayToTeamChat → ITeamChatSender.SendAsync(message) to in-game team chat

toggle @everyone / relay / rename (AlarmComponentModule):
  → IAlarmStore.SetPingEveryoneAsync / SetRelayToTeamChatAsync / RenameAsync → embed refresh

server goes not-Connected (ConnectionStatusChangedEvent):
  → AlarmFireRelay marks that server's alarm embeds ⚠️ Unreachable (toggle buttons disabled)

server removed (RustServer cascade delete):
  → SmartAlarm rows deleted by FK cascade
```


## Components

### 1. Pairing (extend the existing entity path — surgical, Approach A)

- **`PairingNotification`** gains a `PairedEntityKind EntityKind` field (default
  `SmartSwitch` for back-compat). `PairingKind.Entity` still gates entity-vs-server;
  the new field distinguishes switch vs. alarm **within** the entity path. (No
  name/type fields — the typed per-kind events don't carry them.)
- **`RustPlusFcmPairingSource`**:
  - subscribes to **`OnSmartAlarmPairing`** alongside `OnServerPairing` /
    `OnSmartSwitchPairing` — maps `Notification<ulong?>` → `PairingNotification`
    with `Kind=Entity, EntityKind=SmartAlarm`, reading the entity id from
    `Notification.Data` (`ulong?`; `null` → drop) and `FacepunchServerId` from
    `Notification.ServerId`. Same shape as the switch handler.
  - subscribes to **`OnAlarmTriggered`** — maps `Notification<AlarmEvent>` →
    resolve nothing in the source beyond reading `FacepunchServerId`
    (`Notification.ServerId`) and the entity id from the raw `Body.EntityId`; then
    publishes a new **`AlarmTriggeredEvent`** **directly on the bus** (the trigger is
    not a pairing, so it bypasses `PairingHandler` and the per-account
    guild/owner resolution that wraps pairing dispatch — see note below). Title and
    Message come from `AlarmEvent`.
  - All wiring/unwiring goes in the same ctor + `DisposeAsync` spot as the existing
    switch/server handlers; dispatch stays inside the existing broad-catch
    fire-and-forget bridge so a malformed notification cannot fault the listener loop.

  > **Guild/owner resolution for the trigger.** The pairing path resolves
  > `(guildId, ownerUserId)` from the per-account listener context before calling
  > `PairingHandler.HandleAsync`. The trigger needs only `guildId` + `FacepunchServerId`
  > to attribute the fire to a `RustServer` (the same `GetByFacepunchServerIdAsync`
  > lookup the handler uses for entity pairings). The execution plan resolves the
  > guild from the same listener-context the pairing dispatch already has, and
  > publishes `AlarmTriggeredEvent(guildId, serverId, entityId, title, message)` once
  > the server is resolved; an unknown Facepunch server → log + drop, mirroring
  > `HandleEntityAsync`. (Whether the server resolution lives in the source or in a
  > small handler method is a plan-level detail; the seam is the bus event.)

- **`PairingHandler.HandleEntityAsync`** turns its hardcoded `SwitchPairedEvent`
  publish into a 2-arm switch on `notification.EntityKind`:
  `SmartSwitch → SwitchPairedEvent` (unchanged), `SmartAlarm → AlarmPairedEvent`.
  Server resolution (`GetByFacepunchServerIdAsync(guildId, FacepunchServerId)`,
  drop-if-unknown) is shared and unchanged. `StorageMonitor` is not yet routed (4c).
  Because the typed per-kind events already filter to the right kind, Abstractions
  stays free of the FCM package dependency.

### 2. Domain + Persistence — `SmartAlarm` entity

- **`RustPlusBot.Domain/Alarms/SmartAlarm.cs`**:
  `Guid Id`, `ulong GuildId`, `Guid ServerId` (FK→RustServer, **cascade delete**),
  `ulong EntityId`, `string Name` (defaults to a generated `Alarm <EntityId>` —
  the typed FCM event carries no name; renamable), `ulong? MessageId`,
  `ulong PairedByUserId`, `DateTimeOffset CreatedUtc`, and alarm-specific:
  `bool PingEveryone` (default false), `bool RelayToTeamChat` (default false),
  `string? LastTitle`, `string? LastMessage`, `DateTimeOffset? LastFiredUtc`.
  Unique index on `(GuildId, ServerId, EntityId)`.
- `IEntityTypeConfiguration<SmartAlarm>` using the global ulong↔long snowflake
  converter (repo convention).
- Migration **`SmartAlarms`** (the single intended migration for 4b).
- **`IAlarmStore`** (Persistence, `public sealed` impl like `SwitchStore`):
  `AddAsync`, `GetAsync(guild, server, entityId)`, `ListByServerAsync(guild, server)`,
  `RenameAsync`, `SetMessageIdAsync`, `SetPingEveryoneAsync`,
  `SetRelayToTeamChatAsync`, `RecordFiredAsync(guild, server, entityId, title,
  message, firedUtc)`, `RemoveAsync`, `ExistsAsync(guild, server, entityId)`.
- Pending (un-validated) pairings are **in-memory only** (held by the coordinator
  until Accept); only accepted alarms persist.

### 3. `RustPlusBot.Features.Alarms` (new project)

- **`AlarmPairingCoordinator`** — consumes `AlarmPairedEvent`; if not already
  persisted (`IAlarmStore.ExistsAsync`), derives the default name `Alarm <EntityId>`
  and posts the transient "New alarm detected (`Alarm <id>`) — Add it?"
  prompt in `#alarms` (custom-id carries guild/server/entityId), holds pending state
  in-memory. On Accept → persist → replace prompt with the alarm embed. Race-guarded
  via `ExistsAsync` + the unique index. (Structural copy of `SwitchPairingCoordinator`.)
- **`AlarmEmbedRenderer`** (pure) — renders the alarm embed: name; last-fired
  title/message + relative timestamp (or "never fired"); `@everyone` toggle state;
  relay toggle state; ⚠️ Unreachable state. Produces the component rows
  (@everyone toggle, Relay toggle, Rename; toggles disabled when unreachable). Also
  `RenderPrompt` for the Accept/Dismiss prompt. EN/FR.
- **`AlarmComponentModule`** (thin, wildcard custom-ids) — handles `Accept`
  (persist + replace prompt with the embed), `Dismiss`, `alarm-ping-toggle`
  (→ `IAlarmStore.SetPingEveryoneAsync` → refresh), `alarm-relay-toggle`
  (→ `SetRelayToTeamChatAsync` → refresh), `alarm-rename` (opens modal) + rename
  modal submit (→ `RenameAsync` → refresh). Each opens a DI scope for the scoped
  store (repo idiom). No `[RequireUserPermission]` — any guild member.
- **`AlarmFireRelay`** — consumes `AlarmTriggeredEvent` (resolve persisted alarm;
  none → log + drop; else `RecordFiredAsync` + re-render embed; then per flags
  @everyone ping in `#alarms` and/or `ITeamChatSender.SendAsync` to team chat) and
  `ConnectionStatusChangedEvent` (server not-Connected → mark its alarm embeds
  ⚠️ Unreachable, disable toggles; **no** Connected arm — no live state to prime).
  Team-chat relay failures are swallowed/logged and cannot block the embed update or
  the ping.
- **Embed posting via the shared `IChannelEmbedPoster`** (new, in `RustPlusBot.Discord`):
  `Features.Alarms` consumes the shared `DiscordChannelEmbedPoster` (untested Discord
  shim) to ensure/post/edit alarm embed messages in `#alarms` by `MessageId` via async
  `GetChannelAsync`; on a deleted message it re-posts and the caller refreshes
  `MessageId`. The `@everyone` ping is a separate `SendEveryonePingAsync`-style send on
  the same shared poster (content mention with `AllowedMentions` permitting `@everyone`
  only for the ping path; embed edits use `AllowedMentions.None`). No slice-specific
  `DiscordAlarmChannelPoster` — the poster is shared (see Summary item 2).
- **Localization via the shared `ILocalizer`** (new, in `RustPlusBot.Discord`):
  `Features.Alarms` provides only an `AlarmLocalizationCatalog` (its `culture → key →
  value` dictionary, EN/FR) and consumes the shared `Localizer` for lookup — no
  slice-specific `IAlarmLocalizer`/`AlarmLocalizer` class (see Summary item 2).
- **`AlarmsHostedService`** (thin) — two bus loops: `AlarmPairedEvent` →
  coordinator; `AlarmTriggeredEvent` + `ConnectionStatusChangedEvent` → relay.
- **`AlarmsServiceCollectionExtensions.AddAlarms(...)`** — registers the above +
  the `Discord.InteractionModuleAssembly(thisAssembly)` seam (so `DiscordBotService`
  discovers `AlarmComponentModule`), wired into the Host's composition root.

### 4. Workspace

- New per-server **`#alarms` `ChannelSpec`** (key `alarms`, **Interactive** profile —
  users press toggle buttons here) + EN/FR `channel.alarms.name`
  (`alarms` / `alarmes`).
- **`IAlarmChannelLocator` / `AlarmChannelLocator`** (GetChannelIdAsync only,
  30s-TTL cache — copy of `SwitchChannelLocator`) backed by the existing
  `IWorkspaceStore.GetChannelsByKeyAsync`.

## Error handling

- FCM pairing/trigger dispatch sits inside the existing broad-catch fire-and-forget
  bridge (same as `OnServerPairing` / `OnSmartSwitchPairing`) — a malformed
  notification cannot fault the listener loop.
- Unmatched fire (no entity id, or no managed alarm matches) → log + drop.
- Team-chat relay failure → swallow + log; never blocks the embed update or the ping.
- Accept-button race (two users Accept the same prompt): `IAlarmStore.ExistsAsync`
  guard + the unique `(guild,server,entityId)` index make the second a no-op.
- Manually-deleted embed message: the poster catches Discord "unknown message",
  re-posts, and refreshes `MessageId` (Workspace's self-heal idiom).
- A not-Connected server's alarm embeds show ⚠️ Unreachable with toggles disabled
  (the toggle is a persisted preference, so it stays valid; disabling avoids implying
  liveness).
- No exception ever carries a token (no secret state in any thrown message).

## Testing

TDD per task, matching repo conventions.

**Unit / store-level:**

- `AlarmEmbedRenderer` (fired / never-fired / unreachable; @everyone on/off; relay
  on/off; EN/FR).
- `AlarmLocalizationCatalog` keys (EN/FR parity).
- `AlarmPairingCoordinator` (paired alarm → prompt with default `Alarm <id>` name;
  dedupe by `ExistsAsync`; Accept persists + replaces prompt; Accept race no-op).
- `AlarmFireRelay` (record + re-render; ping when `PingEveryone`; relay when
  `RelayToTeamChat`; drop unmatched fire; unreachable-on-disconnect; relay-failure
  is swallowed).
- `IAlarmStore` against the SQLite test harness (add / list / rename / toggles /
  record-fired / remove + cascade-from-RustServer-removal).
- `PairingHandler` entity routing → kind switch (`SmartSwitch → SwitchPairedEvent`,
  `SmartAlarm → AlarmPairedEvent`, unknown-server drop).

**Untested by design (integration shims, documented):**

- `RustPlusFcmPairingSource` alarm pairing + trigger wiring (incl. the
  `Body.EntityId`-on-trigger read to be verified live); `DiscordAlarmChannelPoster`;
  the thin `AlarmComponentModule` / `AlarmsHostedService` (InteractionModuleBase is
  not unit-tested in this repo — logic lives in the testable
  coordinator/relay/store).

**Gates:** full suite + per-assembly counts (every fake/double — `FakeWorkspaceStore`,
the connection/test doubles, and any `ITeamChatSender` substitute in affected tests —
must implement / register new members, or assemblies silently drop tests);
`dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`; build 0/0
strict (`-warnaserror`); no EF drift beyond the one intended `SmartAlarms` migration;
run full builds with `-maxcpucount:1`.

## Server attribution

Same model as 4a, **verified against the shipped 4a code** (not the 4a spec text,
which described an endpoint lookup the shipped `PairingHandler` does not use):

- Entity pairings and alarm triggers carry the **Facepunch** server GUID
  (`Notification.ServerId`). This repo backfills `RustServer.FacepunchServerId` on
  server pairing and resolves entity attribution via
  `IServerService.GetByFacepunchServerIdAsync(guildId, facepunchServerId)`.
- An entity pairing / trigger for an unknown Facepunch server is logged and dropped;
  it never auto-creates a server. No schema change for 4b (the
  `FacepunchServerId` column already exists from 4a's backfill).

## Conventions / standing gotchas (carry-forward)

- Solution file is **`RustPlusBot.slnx`** (not `.sln`) — `dotnet sln add` against it.
- The repo's `RustPlusBot.Discord` namespace shadows Discord.Net's `Discord` — use
  `global::Discord.Embed` etc. in Alarms files that reference Discord.Net types.
- NSubstitute on internal interfaces needs
  `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` in the csproj.
- New events live in **Abstractions** (no project refs, no Discord, no FCM).
- Entities live in **Domain** (not Persistence); stores are `public sealed`.
- `docs/superpowers/` is **gitignored / local-only** — never `git add` the spec/plan.
