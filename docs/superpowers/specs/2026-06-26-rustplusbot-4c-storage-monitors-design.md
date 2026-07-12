# Subsystem 4c — Smart Storage Monitors — Design

**Date:** 2026-06-26
**Branch to cut:** `feat/storage-monitors` off `develop`
**Status:** Design approved; ready for implementation plan.

## Summary

4c is the third smart-device slice, after 4a Smart Switches (PR #18) and 4b Smart Alarms
(PR #23). It adds in-game **Smart Storage Monitors**: pair-in-game → user-validated "Add it?"
prompt → a per-monitor **live contents embed** in a per-server `#storagemonitors` channel, with
**Refresh** and **Rename** interactions. Contents are primed on (re)connect and updated live on the
socket's `OnStorageMonitorTriggered` broadcast.

It mirrors the 4a Switches template verbatim in structure. The one architectural difference is the
**data shape**: a storage monitor carries *contents* (a list of item id + quantity, plus capacity
and decay-protection status), not a boolean on/off. So 4c introduces a storage-specific read seam
and bus event (Approach A, below) alongside — never overloading — the existing boolean device event.

Storage monitors have **no control surface** (you can read them but cannot toggle them), so the only
interactions are Refresh, Rename, and the pairing Accept/Dismiss.

## Decisions (locked in brainstorming)

- **Recycle calc DEFERRED.** rustplusplus computes recycle output + structured wood/stone upkeep
  cost; both need the full subsystem-6 item dataset (craft/recycle/decay tables). 4c ships only what
  the socket natively returns.
- **TC upkeep = protection status + expiry.** For a Tool Cupboard the embed shows
  "Protected — expires in 2d 4h" / "Not protected" from `HasProtection` / `ProtectionExpiry`.
  No cost breakdown (that is subsystem 6).
- **Item names = bundled name-only lookup, NOW.** A trimmed static `items.json` (id → display name,
  from Facepunch's published item manifest) is embedded in the new project, used ONLY for naming. No
  craft/recycle/decay logic. This makes 4c useful standalone and is the natural seed for subsystem 6.
- **Refresh model = trigger-driven + manual Refresh button.** Prime on connect via
  `GetStorageMonitorInfoAsync`; update on `OnStorageMonitorTriggered`; a 🔄 Refresh button re-reads
  on demand. No poll loop, no new options.
- **All alerting DEFERRED.** No low-stock thresholds, no decay-expiry pings, no content-change
  notifications. 4c is a focused contents-embed slice, the same size as 4a/4b.
- **Approach A** (parallel storage-specific event + seam) chosen over generalizing the boolean
  `SmartDeviceTriggeredEvent` (B) or letting the feature own the socket subscription (C).

## API facts (verified by decompiling RustPlusApi 2.0.0-beta.3)

FCM (`RustPlusApi.Fcm.RustPlusFcm`):

- `event EventHandler<Notification<ulong?>>? OnStorageMonitorPairing` — entity id only, exactly like
  `OnSmartSwitchPairing` / `OnSmartAlarmPairing`.

Socket (`RustPlusApi.RustPlus`):

- `Task<Response<StorageMonitorInfo?>> GetStorageMonitorInfoAsync(ulong entityId, CancellationToken)`
  — read; also primes the socket's interest so triggers fire thereafter (same as the switch read).
- `event EventHandler<StorageMonitorEventArg>? OnStorageMonitorTriggered` — fires on the
  `EntityChanged` broadcast whose payload carries item capacity.

Data shapes (`RustPlusApi.Data.Entities`):

- `record StorageMonitorInfo { int? Capacity; bool? HasProtection; DateTime ProtectionExpiry;
  IEnumerable<StorageMonitorItemInfo>? Items; }`
- `sealed record StorageMonitorItemInfo { int Id; int? Quantity; bool? IsItemBlueprint; }`
- `sealed record StorageMonitorEventArg : StorageMonitorInfo { ulong Id; }` (the trigger arg = the
  full info record + the entity id).

Capacity discriminates the physical type: **Tool Cupboard = 24, Large Box = 48, Small Box = 12**;
any other value renders a generic "Storage Monitor" label (forward-safe).

## Architecture (Approach A)

The whole slice is a faithful copy of the 4a Switches pattern. Only the data path differs.

### New Abstractions (dependency-free)

Namespace `RustPlusBot.Features.Connections.Listening` (matches where 2a/3c homed the other socket
DTOs, so consumers' usings stay stable):

- `StorageContentsSnapshot(int? Capacity, bool? HasProtection, DateTimeOffset? ProtectionExpiry,
  IReadOnlyList<StorageItemSnapshot> Items)`
- `StorageItemSnapshot(int ItemId, int Quantity, bool IsBlueprint)`

Namespace `RustPlusBot.Abstractions.Events`:

- `StorageMonitorPairedEvent(ulong GuildId, Guid ServerId, ulong EntityId)` — mirrors `SwitchPairedEvent`.
- `StorageMonitorTriggeredEvent(ulong GuildId, Guid ServerId, ulong EntityId,
  StorageContentsSnapshot Contents)`.

`ProtectionExpiry` is mapped from the socket's `DateTime` as UTC
(`new DateTimeOffset(DateTime.SpecifyKind(expiry, DateTimeKind.Utc))`), and is only meaningful when
`HasProtection == true`; the renderer ignores it otherwise.

### FCM pairing path (`Features.Pairing`)

- `RustPlusFcmPairingSource` subscribes `OnStorageMonitorPairing` → emits a `PairingNotification`
  with `EntityKind = PairedEntityKind.StorageMonitor` (the enum member already exists, unused). Same
  shape as switch/alarm pairing (entity id only; default name `Storage Monitor <id>`).
- `PairingHandler.HandleEntityAsync` already has a `switch (notification.EntityKind)` with
  `SmartSwitch`/`SmartAlarm` arms and a `default` that logs "unrouted kind". Add the third arm:
  `case PairedEntityKind.StorageMonitor:` → publish `StorageMonitorPairedEvent`.

### Socket seam (`Features.Connections`)

- `IRustServerConnection.GetStorageMonitorInfoAsync(ulong entityId, TimeSpan timeout,
  CancellationToken)` → `StorageContentsSnapshot?` (null on failure/timeout). The read also primes
  the socket's interest, exactly like `GetSmartDeviceInfoAsync`.
- New socket-level event `event EventHandler<StorageMonitorTrigger>? StorageMonitorTriggered` on
  `IRustServerConnection`, where `internal sealed record StorageMonitorTrigger(ulong EntityId,
  StorageContentsSnapshot Contents)`. `RustPlusSocketSource` subscribes/unsubscribes RustPlusApi's
  `OnStorageMonitorTriggered` (maps `StorageMonitorEventArg.Id` + the info → the snapshot).
- `IRustServerQuery.GetStorageContentsAsync(ulong guildId, Guid serverId, ulong entityId,
  CancellationToken)` → `StorageContentsSnapshot?` — backs the Refresh button; returns null when
  there is no live socket. Backed by `ConnectionSupervisor` over `_liveSockets`.

### Supervisor (`ConnectionSupervisor`)

- In `EnsureConnectionAsync`, subscribe `connection.StorageMonitorTriggered` (next to
  `SmartDeviceTriggered`); unsubscribe in the `finally`.
- `PublishStorageTriggerAsync` publishes `StorageMonitorTriggeredEvent` (contents are on the arg — no
  re-read), wrapped in the same broad-catch isolation as `PublishDeviceTriggerAsync`.
- `PrimeDevicesAsync` gains a **third arm**: list `IStorageMonitorStore` entities for the
  `(guild, server)`, and for each call `GetStorageMonitorInfoAsync` → publish
  `StorageMonitorTriggeredEvent` (the prime read). Parallel to the switch and alarm arms, each with
  best-effort per-entity error handling.

### New feature project `RustPlusBot.Features.StorageMonitors`

Structural copy of `Features.Switches`. References Abstractions / Persistence / Discord / Domain /
Workspace / Connections (for `IRustServerQuery`) / Localization.

- `Pairing/StorageMonitorPairingCoordinator.cs` — in-memory pending map; `Accept` persists the
  entity, `Dismiss` drops it.
- `Rendering/StorageMonitorEmbedRenderer.cs` — **pure**, injects the shared `ILocalizer` and
  `IItemNameResolver`. Renders type + contents + protection + unreachable.
- `Rendering/StorageMonitorComponentIds.cs` — wildcard custom-ids `storage:refresh:`,
  `storage:rename:`, `storage:accept:`, `storage:dismiss:` (tail `{serverId}:{entityId}`).
- `Modules/StorageMonitorComponentModule.cs` + `Modules/StorageMonitorRenameModal.cs` — thin
  interaction handlers.
- `Relaying/StorageMonitorStateRelay.cs` — consumes `StorageMonitorTriggeredEvent` (load entity →
  render → post) and `ConnectionStatusChangedEvent` (re-render with the ⚠ Unreachable line).
- `Posting/DiscordStorageMonitorChannelPoster.cs` + `Posting/IStorageMonitorChannelPoster.cs` —
  untested shim; self-heals a deleted embed (Discord 404 → repost), copied from
  `DiscordSwitchChannelPoster`.
- `Naming/IItemNameResolver.cs` + `Naming/EmbeddedItemNameResolver.cs` + `Naming/items.json` —
  the item-name lookup (below).
- `Hosting/StorageMonitorsHostedService.cs` — thin; bus loops for `StorageMonitorPairedEvent`,
  `StorageMonitorTriggeredEvent`, `ConnectionStatusChangedEvent`.
- `StorageMonitorServiceCollectionExtensions.cs` — `AddStorageMonitors()`.

Localization: **no per-feature localizer.** PR #26 consolidated all features onto a shared
`RustPlusBot.Localization` (`ILocalizer.Get(key, culture)` / `Get(key, culture, args)`, keys in
`Strings.resx` / `Strings.fr.resx`). 4c adds its keys to the shared `.resx` and injects `ILocalizer`.

### Item-name lookup (`Naming/`)

- `IItemNameResolver.Resolve(int itemId) -> string` — returns the display name, or a stable fallback
  `"Item {id}"` for unknown ids (forward-safe when Facepunch adds items).
- `EmbeddedItemNameResolver` — loads the embedded `items.json` once (lazy singleton) into a
  `FrozenDictionary<int,string>`. Registered singleton in `AddStorageMonitors`.
- `items.json` — embedded resource; trimmed from Facepunch's published item manifest to **only**
  `{ id: displayName }` pairs (no shortnames/images/crafting). Plain factual game data (Facepunch's
  item catalogue), not GPL code — consistent with the project's "use Facepunch data/art, not
  rustplusplus's licensed code" stance.

The renderer depends on the **interface**, so it stays a pure testable unit (fake resolver in
tests); the embedded loader gets its own focused test. Subsystem 6 can later implement this same
interface or supersede it without touching the renderer.

### Domain & persistence

- `Domain/StorageMonitors/SmartStorageMonitor.cs` — `Id`, `GuildId`, `ServerId`, `EntityId`,
  `Name`, `CreatedUtc`. Mirrors `SmartSwitch`. **No persisted device state** — contents are
  live-only, never stored.
- `Persistence`: `public sealed StorageMonitorStore` / `IStorageMonitorStore` — idempotent-race
  `AddAsync` (unique `(GuildId, ServerId, EntityId)` index + `DbUpdateException` recovery),
  client-side `OrderBy(CreatedUtc)` (SQLite cannot ORDER BY DateTimeOffset). `IEntityTypeConfiguration`
  for the entity; new EF migration `SmartStorageMonitors` (FK → RustServer cascade).

### Workspace (`Features.Workspace`)

- New per-server `#storagemonitors` `ChannelSpec` (key `storagemonitors`, **Interactive** profile —
  it has buttons, like `#switches`).
- New shared-`.resx` key `channel.storagemonitors.name` → EN `storage-monitors` /
  FR `moniteurs-stockage` (final FR wording confirmed during implementation; kept within Discord's
  channel-name length limits).
- `StorageMonitorChannelLocator` / `IStorageMonitorChannelLocator` — 30s-TTL cache over
  `IWorkspaceStore.GetChannelsByKeyAsync`, verbatim copy of `SwitchChannelLocator`.

### Host wiring (composition root)

- `AddStorageMonitors()` alongside `AddSwitches()` / `AddAlarms()`.
- Register the new project's `InteractionModuleAssembly` seam so `DiscordBotService` discovers
  `StorageMonitorComponentModule`.
- Shared `ILocalizer` already registered (PR #26) — no change.

### Options, intents, schema

- **No new options** — trigger-driven + manual Refresh; the Refresh read reuses
  `ConnectionOptions.HeartbeatTimeout`.
- **No new gateway intents** — storage rides the existing Rust+ socket; the Discord side is
  buttons/modals covered by current intents.
- One new EF migration (`SmartStorageMonitors`); no other schema change.

## Embed contents (rendering rules)

- **Type** from `Capacity`: 24 → Tool Cupboard, 48 → Large Box, 12 → Small Box, else → Storage Monitor.
- **Contents**: `"{name} ×{quantity}"` per line (names via `IItemNameResolver`); blueprint items
  rendered `"{name} (BP)"`; sorted by quantity descending. Empty list → "Empty".
- **Slots summary**: "{itemCount} / {Capacity} slots" (omit the `/Capacity` if capacity is null).
- **Protection (Tool Cupboard only)**: "Protected — expires in {duration}" using
  `DurationFormat.Compact` against `ProtectionExpiry`, or "Not protected".
- **⚠ Unreachable** line inline when the server is not Connected.
- All strings EN/FR via the shared `ILocalizer`.

## Error handling

- **Failed read** (null/timeout): prime path logs + skips that monitor (best-effort, never crashes
  the connected loop — same broad-catch arms as switch/alarm priming). Refresh → ephemeral
  "couldn't reach the server" reply.
- **No live socket** (not Connected): reads return null → embed shows ⚠ Unreachable;
  `ConnectionStatusChangedEvent` drives the re-render on connect/disconnect.
- **Unknown Facepunch server** on entity pairing: logged + dropped (existing `HandleEntityAsync`).
- **Deleted embed** (Discord 404): poster reposts (self-heal).
- **Unknown item id**: resolver returns `"Item {id}"` — never throws.
- **Idempotent pairing race**: `AddAsync` unique-index + `DbUpdateException` recovery.
- **Socket callbacks**: broad-catch + logged; publish failures never crash the socket thread.

## Testing

TDD per task. New `RustPlusBot.Features.StorageMonitors.Tests`:

- Pairing coordinator: accept persists / dismiss drops / idempotency.
- Embed renderer (with a **fake `IItemNameResolver`**): TC vs box vs unknown capacity;
  protected/unprotected/expiry; contents/empty/blueprint/quantity-sorting; unreachable line; EN/FR.
- State relay: trigger → post; `ConnectionStatusChangedEvent` → unreachable re-render.
- `EmbeddedItemNameResolver`: known id resolves, unknown → fallback, file loads once.

Cross-cutting (recurring lesson — **extend the fakes or the assembly silently drops tests**; run the
full suite with `-maxcpucount:1` and read per-assembly counts):

- `Persistence.Tests`: `StorageMonitorStore` CRUD + unique-index race + cascade-delete with a
  seeded `RustServer`.
- `Connections.Tests`: supervisor primes storage entities + relays `StorageMonitorTriggeredEvent`;
  extend `FakeRustSocketSource` with `GetStorageMonitorInfoAsync` + a scriptable storage trigger.
- `Pairing.Tests`: `HandleEntityAsync` routes `StorageMonitor` → `StorageMonitorPairedEvent`.

Untested by design (integration shims): `RustPlusSocketSource` storage methods,
`DiscordStorageMonitorChannelPoster`.

**Gates** (per repo conventions): `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
zero-diff; build `-warnaserror` clean; full suite green (`-maxcpucount:1`); no unexpected EF model
drift (verify via "no unexpected Migrations/ModelSnapshot changes" if `has-pending-model-changes`
tooling errors).

## Out of scope (deferred)

- Recycle-output calc; structured wood/stone upkeep-cost projection (→ subsystem 6).
- Content-change alerting / low-stock thresholds / decay-expiry pings (→ a later slice).
- Any monitor control (storage monitors have no control surface).
- Switch groups (→ a later 4 slice); cameras (→ subsystem 5).

## NSubstitute / analyzer reminders (from prior device slices)

- NSubstitute on internal interfaces needs `<InternalsVisibleTo Include="DynamicProxyGenAssembly2"/>`
  in any project whose internal types are mocked (Connections already has it; add to the new project
  if a test mocks an internal type).
- Roslynator gotchas seen in 4a/4b: `RCS1141` (XML `<param>` docs on new params), `S1135` (`// TODO`
  is an error — use `<remarks>`), `CA1031` (annotate broad catches), `CA1307`/`CA1310`
  (`StringComparison` on string ops), `S4581` (`Guid.Empty` not `default`).
- The solution is `RustPlusBot.slnx` (not `.sln`); `RustPlusBot.Discord` namespace shadows
  Discord.Net's `Discord` namespace — use `global::Discord.Embed` where both are in scope.
