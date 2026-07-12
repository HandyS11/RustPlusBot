# Subsystem 4a — Smart Switch pairing & control — Design

**Date:** 2026-06-19
**Status:** Approved (brainstorming complete; ready for implementation plan)
**Branch:** `feat/smart-switches` off `develop`

## Summary

Subsystem 4a is the first slice of subsystem 4 (smart devices). It builds the
deferred FCM **entity-pairing** path (ignored since 1b-i), the live-socket
**entity read/control** seam, and **Smart Switches end-to-end**: pair a switch
in-game → validate it in Discord → get a per-switch embed in a per-server
`#switches` channel with ON / OFF / Strobe / Rename controls whose status stays
in sync with the real in-game state.

Alarms, storage monitors, switch groups, a dedicated unreachable-devices
channel, cameras, and in-game/slash command equivalents are explicitly out of
scope (later slices / subsystems).

## Goals

1. Detect a new Smart Switch pairing via the FCM listener (`OnSmartSwitchPairing`).
2. Require **user validation** ("Add it?") before a detected switch becomes managed
   — switches are **not** auto-registered (unlike servers).
3. Persist accepted switches so they survive restarts; re-prime (re-subscribe to)
   each on every (re)connect.
4. One embed per switch in a per-server `#switches` channel, showing live status.
5. Controls per embed: set to the opposite value (on/off), strobe, and **rename**.
6. Reflect real in-game state: prime on connect, then update on the socket's
   `OnSmartSwitchTriggered` change event.
7. Show unreachable/removed switches inline on their embed (⚠️, buttons disabled);
   no separate channel.
8. Any guild member may validate and operate switches (role-gating stays in
   subsystem 9).

## Non-Goals (out of scope for 4a)

- Smart **alarms** (FCM `OnSmartAlarmPairing`/`OnAlarmTriggered`, @everyone toggle) → **4b**.
- **Storage monitors** (contents, TC upkeep; recycle calc needs the item DB) → **4c** / subsystem 6.
- **Switch groups** (collective on/off) → a slice after switches land.
- A dedicated **unreachable-devices channel** — inline status only.
- **Camera** entities → subsystem 5.
- In-game `!`-command and `/`-slash equivalents for switches — the Discord embed
  surface is the whole of 4a; commands can be added later.

## Confirmed RustPlusApi / FCM surface (verified against 2.0.0-beta.2 DLLs)

> **Package version:** 4a targets **`RustPlusApi` / `RustPlusApi.Fcm` 2.0.0-beta.2**
> (bumped in `Directory.Packages.props`). beta.2 **fixes the entity-id type mismatch**
> that beta.1 had: `EntityEvent.EntityId` / `Body.EntityId` are now **`ulong?`** (were
> `int?`), so no `int?→ulong` boundary conversion is needed — the FCM id type lines up
> with the socket's `ulong` calls. The whole solution builds 0/0 on beta.2.

**FCM (`RustPlusApi.Fcm.RustPlusFcm`) events:**

- **Use the typed `OnSmartSwitchPairing`** → `EventHandler<Notification<ulong?>>` — a
  **per-kind** event the package fires only for `EntityType == Switch (1)`, so 4a
  subscribes to exactly the kind it cares about and does **not** branch on entity type
  itself. `Notification.Data` is the entity id as **`ulong?`** (beta.2 — already the
  socket's `ulong`, no conversion); a `null` id → drop. The package also exposes the
  parallel `OnSmartAlarmPairing` / `OnStorageMonitorPairing` for 4b/4c later.
- **Trade-off — no entity name.** The typed event carries the id only; the Rust+
  user-chosen entity name (`Body.EntityName`) is **not** on it. 4a accepts this: the
  pairing prompt and the embed **default to a generic name** (e.g. `Switch <id>`), which
  the user renames via the embed's Rename control. This was a deliberate choice (use the
  pre-filtered typed event) over `OnEntityPairing`, which carries the name but requires
  branching on `EntityType` and threading the name through the pipeline.
- `OnEntityPairing` (the alternative we did **not** take) → `EventHandler<Notification<EntityEvent?>>`,
  where `EntityEvent { ulong? EntityId, string? EntityName, EntityType? EntityType }`
  fires for all three kinds and is the only event that carries `EntityName`. Verified
  against the `RustPlusApi.Fcm` 2.0.0-beta.2 DLL: the typed per-kind events are
  dispatched with `body.ToEntityId()` (id only) and `OnEntityPairing` with
  `body.ToEntityEvent()` (id + name + type).
- `Notification<T> { T? Data, ulong PlayerId, int PlayerToken, Guid ServerId }` — the
  `ServerId` is the **Facepunch** server GUID (not our `RustServer.Id`). The raw
  `Body` carries `Ip`/`Port`/`Name` and is reachable via
  `FcmMessage.Data.Body` (`FcmMessage.Data` is a `MessageData`, `MessageData.Body` is
  the raw `Body`). `Body.EntityId` is also **`ulong?`** in beta.2. Server attribution is
  therefore unambiguous — resolved by endpoint (see the resolved server-attribution
  note below).

**Socket (`RustPlusApi.RustPlus`) methods + events:**

- `Task<Response<SmartSwitchInfo?>> GetSmartSwitchInfoAsync(ulong entityId, CancellationToken)`;
  `SmartSwitchInfo { bool IsActive }`.
- `Task<Response<SmartSwitchInfo?>> SetSmartSwitchValueAsync(ulong smartSwitchId, bool smartSwitchValue, CancellationToken)`.
- `Task<Response<SmartSwitchInfo?>> ToggleSmartSwitchAsync(ulong entityId, CancellationToken)`.
- `Task<Response<SmartSwitchInfo?>> StrobeSmartSwitchAsync(ulong entityId, int timeoutMilliseconds = 1000, bool value = true, CancellationToken)`.
- `event EventHandler<SmartSwitchEventArg> OnSmartSwitchTriggered`;
  `SmartSwitchEventArg : SmartSwitchInfo { ulong Id }` — in beta.2 the arg **inherits
  `SmartSwitchInfo`, so it carries `Id` *and* the new `IsActive`**. The connect-time
  priming re-read (below) is still done to seed state on (re)connect, but on a live
  trigger the new value is available directly on the event arg — re-reading
  `GetSmartSwitchInfoAsync` on trigger is now optional (a confirmation, not required).
- (Subscription model: calling `GetSmartSwitchInfoAsync(id)` for an entity primes
  state and registers the socket's interest, so `OnSmartSwitchTriggered` thereafter
  fires for it. This is why connect-time priming both seeds state and "re-subscribes".)

> `Response<T> { bool IsSuccess, ErrorMessage? Error, T? Data }` — confirmed against the
> beta.2 DLL and by the existing `RustPlusSocketSource` usage of the same shape for
> info/time/team calls. Reuse that idiom; the payload `T` is now nullable
> (`SmartSwitchInfo?`).

## Architecture

Follows the established feature-slice pattern (`Features.Events`, `Features.Players`):
a **new `RustPlusBot.Features.Switches` project** owns the Discord surface and
orchestration, on top of seams extended in **Pairing** (entity-pairing detection)
and **Connections** (live entity read/control), with a new **`SmartSwitch` entity**
in Domain + store in Persistence, and a per-server **`#switches` channel** in Workspace.

Rejected alternatives: switch logic inside Connections (breaks the socket-layer
boundary), or inside Pairing (FCM-only, no socket/Discord concerns).

### Project dependency arrows (no cycles)

- `Features.Switches` → Abstractions, Persistence, Domain, Discord, Workspace, Connections.
- Pairing and Connections extensions stay within their existing project graphs.
- New events live in **Abstractions** (dependency-free), consumed by `Features.Switches`.

### Data flow

```
in-game pair switch
  → FCM OnSmartSwitchPairing (RustPlusFcmPairingSource)   // typed, per-kind, id-only
  → PairingNotification(Kind=Entity, EntityId)            // no name/type on the wire
  → PairingHandler: route Entity → publish SwitchPairedEvent(GuildId, ServerId, EntityId)
  → SwitchPairingCoordinator (Features.Switches): if not already persisted
      → post transient "New switch detected (Switch <id>) — Add it? [Accept][Dismiss]" in #switches
      → user Accept → ISwitchStore.AddAsync (default name "Switch <id>") → replace prompt with the switch embed

on (re)connect (ConnectionSupervisor connected loop):
  → for each persisted switch on that server (ISwitchStore.ListByServerAsync):
      GetSmartSwitchInfoAsync(entityId)   // primes state + subscribes
      → publish SwitchStateChangedEvent(GuildId, ServerId, EntityId, IsActive)
  → socket OnSmartSwitchTriggered(entityId) → re-read info → publish SwitchStateChangedEvent

button press ON/OFF/Strobe (SwitchComponentModule):
  → IRustServerQuery.SetSmartSwitchAsync / StrobeSmartSwitchAsync → embed refresh

rename (SwitchComponentModule → modal):
  → ISwitchStore.RenameAsync → embed refresh

server goes not-Connected (ConnectionStatusChangedEvent):
  → SwitchStateRelay marks that server's switch embeds ⚠️ Unreachable (buttons disabled)

server removed (RustServer cascade delete):
  → SmartSwitch rows deleted by FK cascade
```

## Components

### 1. Pairing (extend existing seams)

- **`RustPlusFcmPairingSource`** also subscribes to **`OnSmartSwitchPairing`**
  (alongside `OnServerPairing`) — the **typed, per-kind** event
  (`EventHandler<Notification<ulong?>>`) the package fires only for switches, so 4a does
  no entity-type branching. (`OnSmartAlarmPairing` / `OnStorageMonitorPairing` are wired
  the same way in 4b/4c.) The handler maps `Notification<ulong?>` → a
  `PairingNotification(Kind=Entity, …)`, reading the **entity id** from
  `Notification.Data` (already `ulong?` in beta.2 — no conversion; `null` id → drop) and
  the server endpoint from the raw `Body` (Ip/Port) / `Notification.ServerId`. The typed
  event carries **no entity name** — the name is defaulted downstream (see the coordinator).
  Dispatch stays inside the existing broad-catch fire-and-forget bridge so a malformed
  notification cannot fault the listener loop.
- **`PairingNotification`** gains a single `ulong EntityId` field (and carries the same
  Ip/Port it already does for the server endpoint); `PairingKind.Entity` (defined since
  1b-i, currently unreachable) is now used. No name/type fields — they aren't on the
  typed event.
- **`PairingHandler`** routes `Kind == Entity` (replacing today's `LogIgnoringKind`
  early-return): resolve the **existing** `RustServer` by endpoint
  `(guildId, Ip, Port)` — a **lookup-only** path (`GetByEndpointAsync` / the unique
  `RustServer (GuildId, Ip, Port)` index), **never create** a server from an entity
  pairing (unknown endpoint → log + drop) — then publish a new
  **`SwitchPairedEvent(ulong GuildId, Guid ServerId, ulong EntityId)`**
  (Abstractions, where `ServerId` is our local `RustServer.Id`). Because the typed
  per-kind event already filters to switches, the event is switch-specific — no
  `EntityType` field, and **Abstractions stays free of the FCM package dependency**.
  Alarm / StorageMonitor pairings simply don't flow through this path in 4a (their typed
  events are subscribed in 4b/4c). See the **Server attribution — RESOLVED** section
  below for the full server-attribution rationale.

### 2. Connections (extend both seams)

- **`IRustServerConnection`** (internal) gains:
  - `Task<bool?> GetSmartSwitchInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken)` — `IsActive`, or `null` on fail/unreachable.
  - `Task<bool> SetSmartSwitchValueAsync(ulong entityId, bool value, TimeSpan timeout, CancellationToken)` — `true` on success.
  - `Task<bool> StrobeSmartSwitchAsync(ulong entityId, int timeoutMs, bool value, TimeSpan timeout, CancellationToken)`.
  - `event EventHandler<ulong>? SmartSwitchTriggered` — carries the entity Id.
- **`RustPlusSocketSource`** (untested integration shim) implements them over
  `RustPlus.GetSmartSwitchInfoAsync` / `SetSmartSwitchValueAsync` /
  `StrobeSmartSwitchAsync`, and forwards `OnSmartSwitchTriggered.Id`. A failed
  call → `null`/`false`; **never** throw with a token in the message.
- **`IRustServerQuery`** (public, Abstractions) gains `(guild, serverId)`-keyed:
  - `Task<bool?> GetSmartSwitchStateAsync(ulong guildId, Guid serverId, ulong entityId, CancellationToken)`.
  - `Task<bool> SetSmartSwitchAsync(ulong guildId, Guid serverId, ulong entityId, bool value, CancellationToken)`.
  - `Task<bool> StrobeSmartSwitchAsync(ulong guildId, Guid serverId, ulong entityId, int timeoutMs, bool value, CancellationToken)`.
  - All return `null`/`false` when there is no live socket. `ConnectionSupervisor`
    implements them over `_liveSockets` (same idiom as `PromoteToLeaderAsync`).
- **`ConnectionSupervisor`** connected loop: on connect, **prime** all persisted
  switches for that server — read each via a new scoped `ISwitchStore.ListByServerAsync`,
  call `GetSmartSwitchInfoAsync` (subscribes + seeds state), publish
  `SwitchStateChangedEvent`. Priming runs **inside** the existing broad-catch loop
  and is best-effort per switch (one failure can't crash the loop or block heartbeat).
  The supervisor forwards `SmartSwitchTriggered` by re-reading info and publishing
  **`SwitchStateChangedEvent(ulong GuildId, Guid ServerId, ulong EntityId, bool IsActive)`** (Abstractions).

### 3. Domain + Persistence — `SmartSwitch` entity

- **`RustPlusBot.Domain/Switches/SmartSwitch.cs`**:
  `Guid Id`, `ulong GuildId`, `Guid ServerId` (FK→RustServer, **cascade delete**),
  `ulong EntityId`, `string Name` (defaults to a generated `Switch <EntityId>` —
  the typed FCM event carries no name; renamable),
  `ulong? MessageId` (Discord embed message), `ulong PairedByUserId`,
  `bool LastIsActive`, `DateTimeOffset CreatedUtc`.
  Unique index on `(GuildId, ServerId, EntityId)`.
- `IEntityTypeConfiguration<SmartSwitch>` using the global ulong↔long snowflake
  converter (repo convention).
- Migration **`SmartSwitches`** (the single intended migration for 4a).
- **`ISwitchStore`** (Persistence, `public sealed` impl like `ConnectionStore`):
  `AddAsync`, `GetAsync(guild, server, entityId)`, `ListByServerAsync(guild, server)`,
  `RenameAsync`, `SetMessageIdAsync`, `UpdateStateAsync`, `RemoveAsync`,
  `ExistsAsync(guild, server, entityId)`.
- Pending (un-validated) pairings are **in-memory only** (held by the coordinator
  until Accept); only accepted switches persist.

### 4. `RustPlusBot.Features.Switches` (new project)

- **`SwitchPairingCoordinator`** — consumes `SwitchPairedEvent`; if not already
  persisted (`ISwitchStore.ExistsAsync`), derives the default name `Switch <EntityId>`
  (the typed event carries no name) and posts the transient
  "New switch detected (Switch &lt;id&gt;) — Add it?" prompt in `#switches` (custom-id
  carries guild/server/entityId) and holds pending state in-memory.
- **`SwitchEmbedRenderer`** (pure) — renders a switch embed: name; status
  ⚡ ON / ⭘ OFF / ⚠️ Unreachable; footer. EN/FR. Also produces the component rows
  (On/Off/Strobe/Rename buttons; disabled when unreachable).
- **`SwitchComponentModule`** (thin, wildcard custom-ids) — handles `Accept`
  (persist + replace prompt with the embed), `Dismiss`, `switch-on` / `switch-off`
  (→ `IRustServerQuery.SetSmartSwitchAsync` → refresh), `switch-strobe`
  (→ `StrobeSmartSwitchAsync`), `switch-rename` (opens modal) + the rename modal
  submit (→ `ISwitchStore.RenameAsync` → refresh). Each opens a DI scope for the
  scoped store (repo idiom). No `[RequireUserPermission]` — any guild member.
- **`SwitchStateRelay`** — consumes `SwitchStateChangedEvent` (update stored
  `LastIsActive` + re-render that switch's embed) and `ConnectionStatusChangedEvent`
  (server not-Connected → mark its switch embeds ⚠️ Unreachable, disable buttons;
  Connected is handled by the supervisor's prime path republishing real state).
- **`ISwitchChannelPoster` + `DiscordSwitchChannelPoster`** (untested Discord shim,
  like `DiscordEventChannelPoster`): ensure/post/edit switch embed messages in
  `#switches` by `MessageId` via async `GetChannelAsync`; on a deleted message
  ("unknown message"), re-post and refresh `MessageId`.
- **`SwitchLocalizationCatalog` / `ISwitchLocalizer` / `SwitchLocalizer`** — EN/FR,
  the localizer-copy pattern (the "consolidate localizers someday" TODO carries forward).
- **`SwitchesHostedService`** (thin) — two bus loops: `SwitchPairedEvent` → coordinator;
  `SwitchStateChangedEvent` + `ConnectionStatusChangedEvent` → relay.

### 5. Workspace

- New per-server **`#switches` `ChannelSpec`** (key `switches`, **Interactive**
  profile — users press buttons here) + EN/FR `channel.switches.name`
  (`switches` / `interrupteurs`).
- **`ISwitchChannelLocator` / `SwitchChannelLocator`** (GetChannelIdAsync only,
  30s-TTL cache — copy of `EventChannelLocator`) backed by the existing
  `IWorkspaceStore.GetChannelsByKeyAsync`.

## Error handling

- All socket access goes through `IRustServerQuery`, returning `null`/`false` when
  there's no live socket or the API fails. The module then shows a brief ephemeral
  "Switch is unreachable right now." and leaves the embed ⚠️. No exception carries a
  token (the shim returns failure rather than throwing with secret state).
- FCM entity-pairing dispatch sits inside the existing broad-catch fire-and-forget
  bridge (same as `OnServerPairing`).
- Connect-time switch priming runs inside the supervisor's connected-loop broad-catch
  and is best-effort per switch (cannot crash the loop or block heartbeat) — the
  marker/team-poll concurrency lesson.
- Accept-button race (two users Accept the same prompt): `ISwitchStore.ExistsAsync`
  guard + the unique `(guild,server,entityId)` index make the second a no-op.
- Manually-deleted embed message: the poster catches Discord "unknown message",
  re-posts, and refreshes `MessageId` (Workspace's self-heal idiom).

## Testing

TDD per task, matching repo conventions.

**Unit / store-level:**

- `SwitchEmbedRenderer` (ON / OFF / unreachable, EN/FR).
- `SwitchLocalizationCatalog` keys.
- `SwitchPairingCoordinator` (paired switch → prompt with default `Switch <id>` name;
  dedupe by ExistsAsync).
- `SwitchStateRelay` (state update; unreachable-on-disconnect).
- `ISwitchStore` against the SQLite test harness (add/list/rename/state/remove +
  cascade-from-RustServer-removal).
- `PairingHandler` entity routing → `SwitchPairedEvent` (incl. unknown-endpoint drop).
- `ConnectionSupervisor` new `IRustServerQuery` switch methods + connect-time
  priming + `SmartSwitchTriggered` → `SwitchStateChangedEvent` publish.

**Untested by design (integration shims, documented):**

- `RustPlusSocketSource` entity mapping; `DiscordSwitchChannelPoster`; the FCM
  `OnSmartSwitchPairing` wiring; the thin `SwitchComponentModule` /
  `SwitchesHostedService` (InteractionModuleBase is not unit-tested in this repo —
  logic lives in the testable coordinator/relay/store).

**Gates:** full suite + per-assembly counts (every fake/double — `FakeRustSocketSource`,
`FakeWorkspaceStore`, the connection/test doubles — must implement the new members,
or assemblies silently drop tests); `dotnet jb cleanupcode RustPlusBot.slnx
--profile=ReformatAndReorder`; build 0/0 strict (`-warnaserror`); no EF drift beyond
the one intended `SmartSwitches` migration; run full builds with `-maxcpucount:1`.

## Server attribution — RESOLVED

The earlier open question ("which server does an entity pairing attribute to, given
the per-account listener?") is **resolved — there is no ambiguity.** Verified against
the `2.0.0-beta.2` DLL:

- Every pairing `Notification<T>` (incl. the typed `Notification<ulong?>` from
  `OnSmartSwitchPairing`) carries `Guid ServerId` (the **Facepunch** server GUID) and the
  raw `Body` carries `Ip`/`Port`/`Name` — independent of which payload `T` is used.
- This repo keys servers by **endpoint** (`RustServer (GuildId, Ip, Port)`, unique
  index) and our `RustServer.Id` is a **local surrogate** `Guid.NewGuid()` — **not**
  the Facepunch GUID, which we don't store.

**Resolution:** the `PairingHandler` resolves the entity's server by endpoint
`(guildId, Ip, Port)` — the same key the server-pairing path already trusts — via a
**lookup-only** path; an entity pairing for an unknown endpoint is logged and
dropped (never auto-creates a server). No schema change for 4a.

**Future hardening (not 4a):** add a nullable `FacepunchServerId` to `RustServer`,
backfill it on server pairing, and resolve entity pairings by the stable GUID first
(endpoint fallback). That also gives the deferred IP-change re-identification
(1b-iv) a stable id. The complete FCM surface (verified shapes) lives inline in the
**Confirmed RustPlusApi / FCM surface** section above.

## Conventions / standing gotchas (carry-forward)

- Solution file is **`RustPlusBot.slnx`** (not `.sln`) — `dotnet sln add` against it.
- The repo's `RustPlusBot.Discord` namespace shadows Discord.Net's `Discord` — use
  `global::Discord.Embed` etc. in Switches files that reference Discord.Net types.
- NSubstitute on internal interfaces needs `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />`.
- New events live in **Abstractions** (no project refs, no Discord).
- `docs/superpowers/` is **gitignored / local-only** — never `git add` the spec/plan.
